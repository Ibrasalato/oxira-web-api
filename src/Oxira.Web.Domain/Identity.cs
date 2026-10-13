namespace Oxira.Web.Domain;

/// <summary>
/// The unified Oxira account (oxira.sa, design.oxira.sa, events.oxira.sa). This API is its identity provider.
/// A user signs in with email + password (and an emailed code when two-step sign-in is on) or with a one-time magic link.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>lower case, unique</summary>
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Company { get; set; } = "";
    /// <summary>uploaded image id (i_…_j)</summary>
    public string? AvatarId { get; set; }
    /// <summary>null for accounts created by a magic link (design.oxira.sa has no passwords)</summary>
    public string? PasswordHash { get; set; }
    public bool EmailVerified { get; set; }
    public bool TwoFactor { get; set; }
    /// <summary>customer | staff | admin</summary>
    public string Role { get; set; } = Roles.Customer;
    /// <summary>changes on password change, role change, suspension and "sign out everywhere": older tokens stop working</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool Suspended { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string Lang { get; set; } = Langs.Default;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>
/// One refresh token. Only its SHA-256 hash is stored. Tokens of one sign-in share a FamilyId (the session id);
/// each refresh rotates the token, and presenting a rotated token again revokes the whole family.
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid FamilyId { get; set; }
    /// <summary>set while the user works on a team owner's account (account switch)</summary>
    public Guid? ActingOwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>when the sign-in (family) started</summary>
    public DateTimeOffset SessionStartedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public long? ReplacedById { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
}

public static class CodePurposes
{
    /// <summary>confirm the email after sign-up</summary>
    public const string Verify = "verify";
    /// <summary>second step of sign-in (two-step sign-in on)</summary>
    public const string Login = "login";
    /// <summary>forgotten password</summary>
    public const string Reset = "reset";
    public static readonly string[] All = [Verify, Login, Reset];
}

/// <summary>A 6-digit code sent by email. Hash only; 5 wrong tries end it.</summary>
public class EmailCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Purpose { get; set; } = CodePurposes.Verify;
    public string CodeHash { get; set; } = "";
    public int Attempts { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>A one-time sign-in link (40 hex characters, hash stored), valid 30 minutes.</summary>
public class MagicLink
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>Someone invited to work on an owner's account: full (everything) or viewer (read only).</summary>
public class TeamMember
{
    public int Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Email { get; set; } = "";
    /// <summary>set once the invited person has a verified Oxira account</summary>
    public Guid? MemberUserId { get; set; }
    public string Role { get; set; } = MemberRoles.Viewer;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
}

/// <summary>Daily usage counters (chat caps, AI quotas). Key + Riyadh day.</summary>
public class UsageCounter
{
    public string Key { get; set; } = "";
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}
