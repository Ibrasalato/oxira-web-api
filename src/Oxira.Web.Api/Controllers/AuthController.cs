using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Auth;

namespace Oxira.Web.Api.Controllers;

/// <summary>
/// The unified Oxira account. Every sign-in returns {success, user, token, expiresAt, refreshToken, refreshExpiresAt};
/// the access token lasts 15 minutes and the refresh token (rotating) 30 days.
/// </summary>
[ApiController, Route("api/auth")]
public class AuthController(ISender mediator) : ControllerBase
{
    /// <summary>Create an account (email, name, password). A 6-digit code is emailed; then POST /verify.</summary>
    [HttpPost("signup"), EnableRateLimiting("auth")]
    public Task<Done> SignUp(SignUpInput body, CancellationToken ct) => mediator.Send(new SignUpCommand(body), ct);

    /// <summary>Confirm the emailed code: purpose verify (after sign-up) or login (two-step sign-in). Signs in.</summary>
    [HttpPost("verify"), EnableRateLimiting("auth")]
    public Task<AuthResult> Verify(VerifyInput body, CancellationToken ct) => mediator.Send(new VerifyCommand(body), ct);

    /// <summary>Email + password. success:false with reason unverified or twofa means a code was emailed (then POST /verify).</summary>
    [HttpPost("login"), EnableRateLimiting("auth")]
    public Task<AuthResult> Login(LoginInput body, CancellationToken ct) => mediator.Send(new LoginCommand(body), ct);

    /// <summary>Send the code again (purpose verify | login | reset).</summary>
    [HttpPost("resend"), EnableRateLimiting("auth")]
    public Task<Done> Resend(ResendInput body, CancellationToken ct) => mediator.Send(new ResendCodeCommand(body), ct);

    /// <summary>Forgotten password: emails a reset code (same answer whether or not the email has an account).</summary>
    [HttpPost("forgot"), EnableRateLimiting("auth")]
    public Task<Done> Forgot(EmailInput body, CancellationToken ct) => mediator.Send(new ForgotPasswordCommand(body), ct);

    /// <summary>Set a new password with the reset code. Ends every other session and signs in.</summary>
    [HttpPost("reset"), EnableRateLimiting("auth")]
    public Task<AuthResult> Reset(ResetInput body, CancellationToken ct) => mediator.Send(new ResetPasswordCommand(body), ct);

    /// <summary>Exchange a refresh token for a new pair (rotation; a reused token ends the whole sign-in).</summary>
    [HttpPost("refresh"), EnableRateLimiting("refresh")]
    public Task<AuthResult> Refresh(RefreshInput body, CancellationToken ct) => mediator.Send(new RefreshCommand(body.RefreshToken), ct);

    /// <summary>Sign out this device, or every device (everywhere: true).</summary>
    [HttpPost("signout")]
    public Task<Done> SignOut(SignOutInput? body, CancellationToken ct) => mediator.Send(new SignOutCommand(body ?? new SignOutInput(null)), ct);

    /// <summary>Same as signout (the website's "logout").</summary>
    [HttpPost("logout"), ApiExplorerSettings(IgnoreApi = true)]
    public Task<Done> Logout(SignOutInput? body, CancellationToken ct) => mediator.Send(new SignOutCommand(body ?? new SignOutInput(null)), ct);

    /// <summary>
    /// Email a one-time sign-in link: returnUrl + ?k=&lt;code&gt; (valid 30 minutes). returnUrl must start with an allowed origin
    /// (Auth:MagicLinkOrigins). A new email gets an account. Always {success:true}.
    /// </summary>
    [HttpPost("magic-link"), EnableRateLimiting("auth")]
    public Task<Done> MagicLink(MagicLinkInput body, CancellationToken ct) => mediator.Send(new MagicLinkCommand(body), ct);

    /// <summary>The k from the magic link → a session.</summary>
    [HttpPost("magic-link/complete"), EnableRateLimiting("auth")]
    public Task<AuthResult> CompleteMagicLink(MagicCompleteInput body, CancellationToken ct) => mediator.Send(new CompleteMagicLinkCommand(body.Code), ct);

    /// <summary>The profile, team memberships and the account being worked on.</summary>
    [HttpGet("me"), Authorize]
    public Task<MeDto> Me(CancellationToken ct) => mediator.Send(new GetMeQuery(), ct);

    /// <summary>Name, phone, company, avatar (an uploaded image id, or "" to remove).</summary>
    [HttpPut("profile"), HttpPost("profile"), Authorize]
    public Task<ProfileResult> Profile(ProfileInput body, CancellationToken ct) => mediator.Send(new UpdateProfileCommand(body), ct);

    /// <summary>Change the password: other devices are signed out; this one gets a new session.</summary>
    [HttpPost("password"), Authorize, EnableRateLimiting("auth")]
    public Task<AuthResult> Password(PasswordInput body, CancellationToken ct) => mediator.Send(new ChangePasswordCommand(body), ct);

    /// <summary>Work on a team owner's account (owner = their email), or back to your own (your email or empty). Returns a new session.</summary>
    [HttpPost("switch"), Authorize]
    public Task<AuthResult> Switch(SwitchInput body, CancellationToken ct) => mediator.Send(new SwitchAccountCommand(body.Owner), ct);

    /// <summary>Signed-in devices.</summary>
    [HttpGet("sessions"), Authorize]
    public Task<SessionsResult> Sessions(CancellationToken ct) => mediator.Send(new ListSessionsQuery(), ct);

    /// <summary>Sign out one device (the id from GET /sessions).</summary>
    [HttpPost("sessions/{id:guid}/revoke"), HttpDelete("sessions/{id:guid}"), Authorize]
    public Task<Done> Revoke(Guid id, CancellationToken ct) => mediator.Send(new RevokeSessionCommand(id), ct);

    /// <summary>Sign out every device; this one gets a new session.</summary>
    [HttpPost("sessions/revoke-all"), Authorize]
    public Task<AuthResult> RevokeAll(CancellationToken ct) => mediator.Send(new RevokeAllSessionsCommand(), ct);

    /// <summary>Two-step sign-in on or off (confirm with the password).</summary>
    [HttpPost("twofa"), Authorize, EnableRateLimiting("auth")]
    public Task<TwoFactorResult> TwoFactor(TwoFactorInput body, CancellationToken ct) => mediator.Send(new SetTwoFactorCommand(body), ct);
}

/// <summary>Oxira accounts (team only): roles and suspension. Both end the user's sessions at once.</summary>
[ApiController, Route("api/admin/users"), Authorize(Policy = "staff")]
public class UsersAdminController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public Task<List<AdminUserDto>> List([FromQuery] string? q, CancellationToken ct) => mediator.Send(new ListUsersQuery(q), ct);

    /// <summary>customer | staff | admin (admins only)</summary>
    [HttpPut("{id:guid}/role")]
    public Task<AdminUserDto> Role(Guid id, RoleInput body, CancellationToken ct) => mediator.Send(new SetRoleCommand(id, body.Role), ct);

    [HttpPut("{id:guid}/suspend")]
    public Task<AdminUserDto> Suspend(Guid id, SuspendInput body, CancellationToken ct) => mediator.Send(new SuspendUserCommand(id, body.Suspended), ct);
}
