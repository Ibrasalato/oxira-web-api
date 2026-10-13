using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Auth;

// ───── inputs (field names match the account page) ─────
public record SignUpInput(string? Email, string? Name, string? Phone, string? Company, string? Password, string? Lang);
public record VerifyInput(string? Email, string? Code, string? Purpose, string? Lang);
public record LoginInput(string? Email, string? Password, string? Lang);
public record ResendInput(string? Email, string? Purpose, string? Lang);
public record EmailInput(string? Email, string? Lang);
public record ResetInput(string? Email, string? Code, string? Password, string? Lang);
public record ProfileInput(string? Name, string? Phone, string? Company, string? Avatar);
public record PasswordInput(string? Current, string? Password);
public record SwitchInput(string? Owner);
public record TwoFactorInput(string? Current, bool? On);
public record RefreshInput(string? RefreshToken);
public record SignOutInput(string? RefreshToken, bool Everywhere = false);
public record MagicLinkInput(string? Email, string? Lang, string? ReturnUrl);
public record MagicCompleteInput(string? Code);
public record RoleInput(string? Role);
public record SuspendInput(bool Suspended);

public record ProfileResult(bool Success, string Avatar, string Name, string Phone, string Company);
public record TwoFactorResult(bool Success, bool Twofa);
public record SessionsResult(bool Success, List<SessionDto> Sessions);
public record AdminUserDto(Guid Id, string Email, string Name, string Role, bool Suspended, bool EmailVerified, DateTimeOffset CreatedAt,
    [property: JsonPropertyName("last_login")] DateTimeOffset? LastLogin);

// ───── messages ─────
public record SignUpCommand(SignUpInput Input) : ICommand<Done>;
public record VerifyCommand(VerifyInput Input) : ICommand<AuthResult>;
public record LoginCommand(LoginInput Input) : ICommand<AuthResult>;
public record ResendCodeCommand(ResendInput Input) : ICommand<Done>;
public record ForgotPasswordCommand(EmailInput Input) : ICommand<Done>;
public record ResetPasswordCommand(ResetInput Input) : ICommand<AuthResult>;
public record RefreshCommand(string? RefreshToken) : ICommand<AuthResult>;
public record SignOutCommand(SignOutInput Input) : ICommand<Done>;
public record GetMeQuery : IQuery<MeDto>;
public record UpdateProfileCommand(ProfileInput Input) : ICommand<ProfileResult>;
public record ChangePasswordCommand(PasswordInput Input) : ICommand<AuthResult>;
public record SwitchAccountCommand(string? Owner) : ICommand<AuthResult>;
public record ListSessionsQuery : IQuery<SessionsResult>;
public record RevokeSessionCommand(Guid Id) : ICommand<Done>;
public record RevokeAllSessionsCommand : ICommand<AuthResult>;
public record SetTwoFactorCommand(TwoFactorInput Input) : ICommand<TwoFactorResult>;
public record MagicLinkCommand(MagicLinkInput Input) : ICommand<Done>;
public record CompleteMagicLinkCommand(string? Code) : ICommand<AuthResult>;
public record ListUsersQuery(string? Q) : IQuery<List<AdminUserDto>>;
public record SetRoleCommand(Guid UserId, string? Role) : ICommand<AdminUserDto>;
public record SuspendUserCommand(Guid UserId, bool Suspended) : ICommand<AdminUserDto>;

/// <summary>Emailed 6-digit codes: hashed, one live code per email and purpose, throttled, 5 tries.</summary>
public class EmailCodes(IAppDbContext db, TimeProvider clock, IEmailSender email, IOptions<AuthOptions> opt)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private static string Hash(Guid id, string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}:{code}")));

    /// <summary>seconds until another code may be sent (0 = now)</summary>
    public async Task<int> WaitAsync(string to, string purpose, CancellationToken ct)
    {
        var o = opt.Value;
        var hourAgo = Now.AddHours(-1);
        var recent = await db.EmailCodes.Where(c => c.Email == to && c.Purpose == purpose && c.CreatedAt > hourAgo)
            .OrderByDescending(c => c.CreatedAt).Select(c => c.CreatedAt).ToListAsync(ct);
        if (recent.Count > 0 && Now - recent[0] < TimeSpan.FromSeconds(o.ResendSeconds))
            return (int)Math.Ceiling(o.ResendSeconds - (Now - recent[0]).TotalSeconds);
        if (recent.Count >= o.CodesPerHour) return (int)Math.Ceiling((recent[^1].AddHours(1) - Now).TotalSeconds);
        return 0;
    }

    /// <summary>sends a new code (older ones stop working). throwWait=false: silently skip when throttled.</summary>
    public async Task SendAsync(string to, string purpose, string lang, bool throwWait, CancellationToken ct)
    {
        var wait = await WaitAsync(to, purpose, ct);
        if (wait > 0)
        {
            if (throwWait) throw new DomainException("انتظر قليلًا قبل طلب رمز جديد", "wait", 429, new { seconds = wait });
            return;
        }
        await db.EmailCodes.Where(c => c.Email == to && c.Purpose == purpose && !c.Used).ExecuteUpdateAsync(s => s.SetProperty(c => c.Used, true), ct);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var row = new EmailCode { Email = to, Purpose = purpose, CreatedAt = Now, ExpiresAt = Now.AddMinutes(opt.Value.CodeMinutes) };
        row.CodeHash = Hash(row.Id, code);
        db.EmailCodes.Add(row);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.Code(to, code, purpose, lang));
    }

    /// <summary>A wrong code is counted and saved, so guessing stops after the allowed tries.</summary>
    public async Task ConsumeAsync(string to, string? code, string purpose, CancellationToken ct)
    {
        var digits = Digits(code);
        if (digits.Length != 6) throw new DomainException("أدخل الرمز المكوّن من 6 أرقام", "code");
        var c = await db.EmailCodes.Where(x => x.Email == to && x.Purpose == purpose && !x.Used).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (c == null || Now > c.ExpiresAt) throw new DomainException("انتهت صلاحية الرمز، اطلب رمزًا جديدًا", "expired");
        var max = opt.Value.CodeAttempts;
        if (c.Attempts >= max) { c.Used = true; await db.SaveChangesAsync(ct); throw new DomainException("تجاوزت عدد المحاولات، اطلب رمزًا جديدًا", "tries"); }
        c.Attempts += 1;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(c.CodeHash), Encoding.ASCII.GetBytes(Hash(c.Id, digits))))
        {
            if (c.Attempts >= max) c.Used = true;
            await db.SaveChangesAsync(ct);
            if (c.Used) throw new DomainException("تجاوزت عدد المحاولات، اطلب رمزًا جديدًا", "tries");
            throw new DomainException("الرمز غير صحيح", "code", 400, new { left = max - c.Attempts });
        }
        c.Used = true;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Sign-up, sign-in (password, emailed code, magic link), sessions and account settings.</summary>
public class AuthHandlers(IAppDbContext db, TimeProvider clock, Access access, ICurrentUser current, IPasswordHasher hasher, Sessions sessions,
    EmailCodes codes, IEmailSender email, IOptions<AuthOptions> opt)
    : IRequestHandler<SignUpCommand, Done>, IRequestHandler<VerifyCommand, AuthResult>, IRequestHandler<LoginCommand, AuthResult>,
      IRequestHandler<ResendCodeCommand, Done>, IRequestHandler<ForgotPasswordCommand, Done>, IRequestHandler<ResetPasswordCommand, AuthResult>,
      IRequestHandler<RefreshCommand, AuthResult>, IRequestHandler<SignOutCommand, Done>, IRequestHandler<GetMeQuery, MeDto>,
      IRequestHandler<UpdateProfileCommand, ProfileResult>, IRequestHandler<ChangePasswordCommand, AuthResult>, IRequestHandler<SwitchAccountCommand, AuthResult>,
      IRequestHandler<ListSessionsQuery, SessionsResult>, IRequestHandler<RevokeSessionCommand, Done>, IRequestHandler<RevokeAllSessionsCommand, AuthResult>,
      IRequestHandler<SetTwoFactorCommand, TwoFactorResult>, IRequestHandler<MagicLinkCommand, Done>, IRequestHandler<CompleteMagicLinkCommand, AuthResult>,
      IRequestHandler<ListUsersQuery, List<AdminUserDto>>, IRequestHandler<SetRoleCommand, AdminUserDto>, IRequestHandler<SuspendUserCommand, AdminUserDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private static DomainException Invalid() => new("البريد الإلكتروني أو كلمة المرور غير صحيحة", "invalid");
    private static DomainException BadEmail() => new("البريد الإلكتروني غير صحيح", "email");
    private static DomainException Weak() => new("كلمة المرور 8 أحرف على الأقل وفيها حرف ورقم", "weak");

    /// <summary>Auth:AdminEmails — accounts that are Oxira admins from their first sign-in</summary>
    private bool IsAdminEmail(string mail) =>
        opt.Value.AdminEmails.Any(a => string.Equals(a.Trim(), mail, StringComparison.OrdinalIgnoreCase));

    private async Task<User?> ByEmailAsync(string mail, CancellationToken ct) => await db.Users.FirstOrDefaultAsync(u => u.Email == mail, ct);

    /// <summary>pending team invitations for this email become memberships once the email is verified</summary>
    private async Task JoinInvitesAsync(User u, CancellationToken ct)
    {
        if (!u.EmailVerified) return;
        var now = Now;
        await db.TeamMembers.Where(t => t.Email == u.Email && t.MemberUserId == null && t.OwnerId != u.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MemberUserId, u.Id).SetProperty(t => t.JoinedAt, now), ct);
    }

    private User NewUser(string mail, string lang) => new()
    {
        Email = mail, Lang = Langs.Of(lang), CreatedAt = Now, Role = IsAdminEmail(mail) ? Roles.Admin : Roles.Customer,
    };

    // ───── sign up and verify ─────
    public async Task<Done> Handle(SignUpCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var mail = Text.Email(i.Email);
        var name = Line(i.Name, 80);
        if (name.Length < 2) throw new DomainException("اكتب اسمك", "name");
        if (!IsEmail(mail)) throw BadEmail();
        if (!IsStrongPassword(i.Password)) throw Weak();
        var lang = Langs.Of(i.Lang);
        var u = await ByEmailAsync(mail, ct);
        if (u is { EmailVerified: true }) throw new DomainException("هذا البريد مسجّل، سجّل الدخول", "exists", 409);
        if (u == null) { u = NewUser(mail, lang); db.Users.Add(u); }
        // an unverified sign-up can be repeated (wrong password the first time, lost code)
        u.Name = name;
        u.Phone = Line(i.Phone, 20);
        u.Company = Line(i.Company, 120);
        u.PasswordHash = hasher.Hash(i.Password!);
        u.Lang = lang;
        await db.SaveChangesAsync(ct);
        await codes.SendAsync(mail, CodePurposes.Verify, lang, throwWait: true, ct);
        return new Done();
    }

    public async Task<AuthResult> Handle(VerifyCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var mail = Text.Email(i.Email);
        var purpose = i.Purpose == CodePurposes.Login ? CodePurposes.Login : CodePurposes.Verify;
        if (!IsEmail(mail)) throw BadEmail();
        var u = await ByEmailAsync(mail, ct);
        if (u == null || u.Suspended) throw new DomainException("انتهت صلاحية الرمز، اطلب رمزًا جديدًا", "expired");
        await codes.ConsumeAsync(mail, i.Code, purpose, ct);
        u.EmailVerified = true;
        u.FailedLogins = 0;
        u.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        await JoinInvitesAsync(u, ct);
        return await sessions.IssueAsync(u, ct: ct);
    }

    public async Task<AuthResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var mail = Text.Email(i.Email);
        if (!IsEmail(mail)) throw BadEmail();
        var u = await ByEmailAsync(mail, ct);
        if (u == null || u.PasswordHash == null || u.Suspended) throw Invalid();
        var o = opt.Value;
        if (u.LockedUntil is { } until && until > Now)
            throw new DomainException("تم إيقاف الدخول مؤقتًا بسبب محاولات كثيرة", "locked", 429, new { minutes = (int)Math.Ceiling((until - Now).TotalMinutes) });
        if (!hasher.Verify(u.PasswordHash, i.Password ?? ""))
        {
            u.FailedLogins += 1;
            if (u.FailedLogins >= o.MaxFailedLogins)
            {
                u.FailedLogins = 0;
                u.LockedUntil = Now.AddMinutes(o.LockMinutes);
                await db.SaveChangesAsync(ct);
                throw new DomainException("تم إيقاف الدخول مؤقتًا بسبب محاولات كثيرة", "locked", 429, new { minutes = o.LockMinutes });
            }
            await db.SaveChangesAsync(ct);
            throw Invalid();
        }
        u.FailedLogins = 0;
        u.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        var lang = Langs.Of(i.Lang ?? u.Lang);
        if (!u.EmailVerified)
        {
            await codes.SendAsync(mail, CodePurposes.Verify, lang, throwWait: false, ct);
            return new AuthResult(false, "unverified");
        }
        if (u.TwoFactor)
        {
            await codes.SendAsync(mail, CodePurposes.Login, lang, throwWait: false, ct);
            return new AuthResult(false, "twofa");
        }
        await JoinInvitesAsync(u, ct);
        return await sessions.IssueAsync(u, ct: ct);
    }

    public async Task<Done> Handle(ResendCodeCommand cmd, CancellationToken ct)
    {
        var mail = Text.Email(cmd.Input.Email);
        if (!IsEmail(mail)) throw BadEmail();
        var purpose = CodePurposes.All.Contains(cmd.Input.Purpose) ? cmd.Input.Purpose! : CodePurposes.Verify;
        var u = await ByEmailAsync(mail, ct);
        // the same answer for unknown emails (no account discovery); throttling still applies
        if (u == null || u.Suspended || purpose == CodePurposes.Verify && u.EmailVerified)
        {
            var wait = await codes.WaitAsync(mail, purpose, ct);
            if (wait > 0) throw new DomainException("انتظر قليلًا قبل طلب رمز جديد", "wait", 429, new { seconds = wait });
            return new Done();
        }
        await codes.SendAsync(mail, purpose, Langs.Of(cmd.Input.Lang ?? u.Lang), throwWait: true, ct);
        return new Done();
    }

    public async Task<Done> Handle(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        var mail = Text.Email(cmd.Input.Email);
        if (!IsEmail(mail)) throw BadEmail();
        var u = await ByEmailAsync(mail, ct);
        if (u == null || u.Suspended)
        {
            var wait = await codes.WaitAsync(mail, CodePurposes.Reset, ct);
            if (wait > 0) throw new DomainException("انتظر قليلًا قبل طلب رمز جديد", "wait", 429, new { seconds = wait });
            return new Done();
        }
        await codes.SendAsync(mail, CodePurposes.Reset, Langs.Of(cmd.Input.Lang ?? u.Lang), throwWait: true, ct);
        return new Done();
    }

    public async Task<AuthResult> Handle(ResetPasswordCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var mail = Text.Email(i.Email);
        if (!IsEmail(mail)) throw BadEmail();
        if (!IsStrongPassword(i.Password)) throw Weak();
        var u = await ByEmailAsync(mail, ct);
        if (u == null || u.Suspended) throw new DomainException("انتهت صلاحية الرمز، اطلب رمزًا جديدًا", "expired");
        await codes.ConsumeAsync(mail, i.Code, CodePurposes.Reset, ct);
        u.PasswordHash = hasher.Hash(i.Password!);
        u.EmailVerified = true;
        u.FailedLogins = 0;
        u.LockedUntil = null;
        await sessions.RevokeAllAsync(u, "password-reset", ct);
        await db.SaveChangesAsync(ct);
        await JoinInvitesAsync(u, ct);
        email.Enqueue(Templates.PasswordChanged(u.Email, u.Lang));
        return await sessions.IssueAsync(u, ct: ct);
    }

    // ───── tokens ─────
    public Task<AuthResult> Handle(RefreshCommand cmd, CancellationToken ct) => sessions.RefreshAsync(cmd.RefreshToken, ct);

    public async Task<Done> Handle(SignOutCommand cmd, CancellationToken ct)
    {
        var me = await access.MaybeAsync(ct);
        if (cmd.Input.Everywhere && me != null)
        {
            await sessions.RevokeAllAsync(me, "signout-all", ct);
            await db.SaveChangesAsync(ct);
            return new Done();
        }
        if (!string.IsNullOrWhiteSpace(cmd.Input.RefreshToken)) await sessions.RevokeAsync(cmd.Input.RefreshToken, ct);
        else if (me != null && current.SessionId is { } sid) await sessions.RevokeFamilyOfAsync(me.Id, sid, "signout", ct);
        return new Done();
    }

    // ───── magic link (design.oxira.sa) ─────
    private bool AllowedReturn(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out _) &&
        opt.Value.Origins.Select(o => o.TrimEnd('/')).Any(o =>
            url.Equals(o, StringComparison.OrdinalIgnoreCase) || url.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith(o + "?", StringComparison.OrdinalIgnoreCase) || url.StartsWith(o + "#", StringComparison.OrdinalIgnoreCase));

    public static string WithCode(string returnUrl, string code)
    {
        var hash = "";
        var at = returnUrl.IndexOf('#');
        if (at >= 0) { hash = returnUrl[at..]; returnUrl = returnUrl[..at]; }
        return $"{returnUrl}{(returnUrl.Contains('?') ? "&" : "?")}k={code}{hash}";
    }

    public async Task<Done> Handle(MagicLinkCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var mail = Text.Email(i.Email);
        if (!IsEmail(mail)) throw BadEmail();
        var returnUrl = Clip(i.ReturnUrl, 1000);
        if (!AllowedReturn(returnUrl)) throw new DomainException("رابط الرجوع غير مسموح", "invalid");
        var lang = Langs.Of(i.Lang);
        // throttle quietly: one link per 2 minutes and 5 per hour per email
        var hourAgo = Now.AddHours(-1);
        var recent = await db.MagicLinks.Where(m => m.Email == mail && m.CreatedAt > hourAgo).Select(m => m.CreatedAt).ToListAsync(ct);
        if (recent.Count >= 5 || recent.Any(t => Now - t < TimeSpan.FromMinutes(2))) return new Done();
        var u = await ByEmailAsync(mail, ct);
        if (u == null)
        {
            u = NewUser(mail, lang);
            u.EmailVerified = true; // whoever opens the emailed link owns the address
            db.Users.Add(u);
        }
        if (u.Suspended) return new Done();
        var code = Hex(20);
        db.MagicLinks.Add(new MagicLink
        {
            Email = mail, UserId = u.Id, CodeHash = Sessions.Hash(code), ReturnUrl = returnUrl, CreatedAt = Now, ExpiresAt = Now.AddMinutes(opt.Value.MagicLinkMinutes),
        });
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.Magic(mail, WithCode(returnUrl, code), lang));
        return new Done();
    }

    public async Task<AuthResult> Handle(CompleteMagicLinkCommand cmd, CancellationToken ct)
    {
        var code = (cmd.Code ?? "").Trim().ToLowerInvariant();
        var expired = new DomainException("انتهت صلاحية الرابط، اطلب رابطًا جديدًا", "expired");
        if (code.Length != 40 || !code.All(char.IsAsciiHexDigitLower)) throw expired;
        var hash = Sessions.Hash(code);
        await using var uow = await db.BeginAsync([$"ml:{hash}"], ct);
        var link = await db.MagicLinks.FirstOrDefaultAsync(m => m.CodeHash == hash, ct);
        if (link == null || link.UsedAt != null || Now > link.ExpiresAt) throw expired;
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == link.UserId, ct);
        if (u == null || u.Suspended) throw expired;
        link.UsedAt = Now;
        u.EmailVerified = true;
        await db.SaveChangesAsync(ct);
        await JoinInvitesAsync(u, ct);
        var result = await sessions.IssueAsync(u, ct: ct);
        await uow.CommitAsync(ct);
        return result;
    }

    // ───── the account ─────
    public async Task<MeDto> Handle(GetMeQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var u = a.Me;
        var memberships = await (from t in db.TeamMembers.AsNoTracking()
                                 join o in db.Users.AsNoTracking() on t.OwnerId equals o.Id
                                 where t.MemberUserId == u.Id && t.JoinedAt != null && !o.Suspended
                                 orderby o.Email
                                 select new MembershipDto(o.Email, t.Role)).ToListAsync(ct);
        return new MeDto(true, u.Id, u.Email, u.Name, u.Phone, u.Company, u.AvatarId ?? "", u.PasswordHash != null, u.TwoFactor,
            u.CreatedAt.ToString("yyyy-MM-dd"), u.LastLoginAt?.UtcDateTime.ToString("O"), a.Role, u.Role, Roles.IsStaff(u.Role),
            memberships, a.IsOwner ? null : a.Owner.Email, u.EmailVerified);
    }

    public async Task<ProfileResult> Handle(UpdateProfileCommand cmd, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        var i = cmd.Input;
        if (i.Name != null)
        {
            var name = Line(i.Name, 80);
            if (name.Length < 2) throw new DomainException("اكتب اسمك", "name");
            u.Name = name;
        }
        if (i.Phone != null) u.Phone = Line(i.Phone, 20);
        if (i.Company != null) u.Company = Line(i.Company, 120);
        if (i.Avatar != null)
        {
            var id = i.Avatar.Trim();
            if (id != "" && !Images.IsId(id)) throw new DomainException("الصورة غير صالحة", "bad_request");
            u.AvatarId = id == "" ? null : id;
        }
        await db.SaveChangesAsync(ct);
        return new ProfileResult(true, u.AvatarId ?? "", u.Name, u.Phone, u.Company);
    }

    public async Task<AuthResult> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var u = a.Me;
        if (u.PasswordHash != null && !hasher.Verify(u.PasswordHash, cmd.Input.Current ?? "")) throw new DomainException("كلمة المرور الحالية غير صحيحة", "invalid");
        if (!IsStrongPassword(cmd.Input.Password)) throw Weak();
        u.PasswordHash = hasher.Hash(cmd.Input.Password!);
        await sessions.RevokeAllAsync(u, "password", ct); // every other device is signed out
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.PasswordChanged(u.Email, u.Lang));
        return await sessions.IssueAsync(u, actingOwnerId: a.IsOwner ? null : a.Owner.Id, ct: ct);
    }

    /// <summary>Work on a team owner's account (or back to one's own): a new session for this device, the old one ends.</summary>
    public async Task<AuthResult> Handle(SwitchAccountCommand cmd, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        var owner = Text.Email(cmd.Owner);
        Guid? acting = null;
        if (owner != "" && owner != me.Email)
        {
            acting = await (from t in db.TeamMembers
                            join o in db.Users on t.OwnerId equals o.Id
                            where t.MemberUserId == me.Id && t.JoinedAt != null && o.Email == owner && !o.Suspended
                            select (Guid?)o.Id).FirstOrDefaultAsync(ct)
                ?? throw DomainException.Forbidden("لست عضوًا في هذا الحساب");
        }
        if (current.SessionId is { } sid) await sessions.RevokeFamilyOfAsync(me.Id, sid, "switch", ct);
        return await sessions.IssueAsync(me, actingOwnerId: acting, ct: ct);
    }

    public async Task<SessionsResult> Handle(ListSessionsQuery q, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        var now = Now;
        var rows = await db.RefreshTokens.AsNoTracking().Where(x => x.UserId == me.Id && x.RevokedAt == null && x.ExpiresAt > now).ToListAsync(ct);
        var owners = rows.Where(r => r.ActingOwnerId != null).Select(r => r.ActingOwnerId!.Value).Distinct().ToList();
        var emails = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var list = rows.GroupBy(r => r.FamilyId).Select(g => g.OrderByDescending(r => r.CreatedAt).First())
            .OrderByDescending(r => r.FamilyId == current.SessionId).ThenByDescending(r => r.CreatedAt)
            .Select(r => new SessionDto(r.FamilyId, Devices.Describe(r.UserAgent), r.SessionStartedAt, r.CreatedAt, r.CreatedByIp, r.FamilyId == current.SessionId,
                r.ActingOwnerId is { } o && emails.TryGetValue(o, out var e) ? e : null))
            .ToList();
        return new SessionsResult(true, list);
    }

    public async Task<Done> Handle(RevokeSessionCommand cmd, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        await sessions.RevokeFamilyOfAsync(me.Id, cmd.Id, "revoked", ct);
        return new Done();
    }

    /// <summary>signs out every device: new security stamp, then a fresh session for this one</summary>
    public async Task<AuthResult> Handle(RevokeAllSessionsCommand cmd, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        await sessions.RevokeAllAsync(a.Me, "revoke-all", ct);
        await db.SaveChangesAsync(ct);
        return await sessions.IssueAsync(a.Me, actingOwnerId: a.IsOwner ? null : a.Owner.Id, ct: ct);
    }

    public async Task<TwoFactorResult> Handle(SetTwoFactorCommand cmd, CancellationToken ct)
    {
        var u = await access.MeAsync(ct);
        if (u.PasswordHash == null || !hasher.Verify(u.PasswordHash, cmd.Input.Current ?? "")) throw new DomainException("كلمة المرور غير صحيحة", "invalid");
        u.TwoFactor = cmd.Input.On ?? !u.TwoFactor;
        await db.SaveChangesAsync(ct);
        return new TwoFactorResult(true, u.TwoFactor);
    }

    // ───── the Oxira team: roles and suspension (both end the user's sessions) ─────
    private static AdminUserDto AdminDto(User u) => new(u.Id, u.Email, u.Name, u.Role, u.Suspended, u.EmailVerified, u.CreatedAt, u.LastLoginAt);

    public async Task<List<AdminUserDto>> Handle(ListUsersQuery q, CancellationToken ct)
    {
        await access.StaffAsync(ct);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(u => u.Email.Contains(q.Q.Trim()) || u.Name.Contains(q.Q.Trim()));
        return (await query.OrderByDescending(u => u.CreatedAt).Take(200).ToListAsync(ct)).Select(AdminDto).ToList();
    }

    public async Task<AdminUserDto> Handle(SetRoleCommand cmd, CancellationToken ct)
    {
        var admin = await access.MeAsync(ct);
        if (admin.Role != Roles.Admin) throw DomainException.Forbidden("هذه العملية لمدير النظام فقط");
        if (!Roles.All.Contains(cmd.Role)) throw new DomainException("دور غير معروف", "bad_request");
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == cmd.UserId, ct) ?? throw DomainException.NotFound("المستخدم غير موجود");
        if (u.Role != cmd.Role)
        {
            u.Role = cmd.Role!;
            await sessions.RevokeAllAsync(u, "role", ct);
            await db.SaveChangesAsync(ct);
        }
        return AdminDto(u);
    }

    public async Task<AdminUserDto> Handle(SuspendUserCommand cmd, CancellationToken ct)
    {
        var admin = await access.MeAsync(ct);
        if (admin.Role != Roles.Admin) throw DomainException.Forbidden("هذه العملية لمدير النظام فقط");
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == cmd.UserId, ct) ?? throw DomainException.NotFound("المستخدم غير موجود");
        if (u.Id == admin.Id) throw new DomainException("لا يمكنك إيقاف حسابك", "bad_request");
        u.Suspended = cmd.Suspended;
        await sessions.RevokeAllAsync(u, cmd.Suspended ? "suspended" : "unsuspended", ct);
        await db.SaveChangesAsync(ct);
        return AdminDto(u);
    }
}

/// <summary>"Chrome · Windows" from a User-Agent.</summary>
public static class Devices
{
    public static string Describe(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return "Unknown";
        string os = ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS" : ua.Contains("Android") ? "Android" : ua.Contains("Windows") ? "Windows"
            : ua.Contains("Mac OS") ? "macOS" : ua.Contains("Linux") ? "Linux" : "";
        string browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") ? "Opera" : ua.Contains("SamsungBrowser") ? "Samsung Internet" : ua.Contains("Firefox/") ? "Firefox"
            : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "";
        var s = string.Join(" · ", new[] { browser, os }.Where(x => x != ""));
        return s == "" ? Line(ua, 40) : s;
    }
}

/// <summary>Uploaded image ids: i_&lt;hex&gt;_j (JPEG) or i_&lt;hex&gt;_p (PNG).</summary>
public static partial class Images
{
    [System.Text.RegularExpressions.GeneratedRegex("^i_[a-f0-9]{20,40}_[jp]$")] private static partial System.Text.RegularExpressions.Regex IdRe();
    public static bool IsId(string? id) => id != null && IdRe().IsMatch(id);
}
