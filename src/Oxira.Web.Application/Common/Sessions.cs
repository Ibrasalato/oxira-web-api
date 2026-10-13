using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Common;

public record UserDto(Guid Id, string Email, string Name, string Role, string? Avatar, bool EmailVerified);

/// <summary>
/// A sign-in: access token (JWT, 15 minutes) and refresh token (30 days, rotating). camelCase JSON:
/// {success, user, token, expiresAt, refreshToken, refreshExpiresAt}. When Success is false, Reason says why
/// (unverified, twofa) and the website moves to the matching step.
/// </summary>
public record AuthResult(
    bool Success, string? Reason = null, UserDto? User = null, string? Token = null, DateTimeOffset? ExpiresAt = null,
    string? RefreshToken = null, DateTimeOffset? RefreshExpiresAt = null);

/// <summary>{"success":true}</summary>
public record Done(bool Success = true);

/// <summary>
/// Sign-in sessions: a short-lived JWT access token plus a rotating refresh token.
/// Refresh tokens are random 256-bit values; only their SHA-256 hash is stored. Each refresh rotates the token,
/// and presenting an already-rotated token (a stolen copy) revokes the whole family.
/// </summary>
public class Sessions(IAppDbContext db, TimeProvider clock, ITokenIssuer tokens, ICurrentUser current)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static UserDto Dto(User u) => new(u.Id, u.Email, u.Name, u.Role, u.AvatarId, u.EmailVerified);

    /// <summary>a new sign-in (new family) or the next token of an existing one</summary>
    public async Task<AuthResult> IssueAsync(User u, Guid? family = null, Guid? actingOwnerId = null, DateTimeOffset? startedAt = null, CancellationToken ct = default)
    {
        var refresh = NewToken();
        var row = new RefreshToken
        {
            UserId = u.Id, TokenHash = Hash(refresh), FamilyId = family ?? Guid.NewGuid(), ActingOwnerId = actingOwnerId,
            CreatedAt = Now, SessionStartedAt = startedAt ?? Now, ExpiresAt = Now + tokens.RefreshLifetime,
            CreatedByIp = Clip(current.Ip, 64), UserAgent = Clip(current.UserAgent, 300),
        };
        db.RefreshTokens.Add(row);
        if (family == null) u.LastLoginAt = Now;
        await db.SaveChangesAsync(ct);
        var access = tokens.Issue(u, row.FamilyId, actingOwnerId);
        return new AuthResult(true, null, Dto(u), access.Token, access.ExpiresAt, refresh, row.ExpiresAt);
    }

    public async Task<AuthResult> RefreshAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw DomainException.Session();
        var hash = Hash(token);
        await using var uow = await db.BeginAsync([$"rt:{hash}"], ct);
        var row = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct) ?? throw DomainException.Session();
        if (row.RevokedAt != null)
        {
            if (row.ReplacedById != null)
            {
                // a rotated token came back: someone has a copy. End every session of this sign-in.
                await RevokeFamilyAsync(row.FamilyId, "reuse", ct);
                await uow.CommitAsync(ct);
            }
            throw DomainException.Session();
        }
        if (Now >= row.ExpiresAt) throw DomainException.Session();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == row.UserId, ct);
        if (u == null || u.Suspended) throw DomainException.Session("هذا الحساب موقوف");
        // a switched session keeps working on the owner's account only while the membership exists
        var acting = row.ActingOwnerId;
        if (acting != null && !await db.TeamMembers.AnyAsync(t => t.OwnerId == acting && t.MemberUserId == u.Id && t.JoinedAt != null, ct)) acting = null;
        var next = await IssueAsync(u, row.FamilyId, acting, row.SessionStartedAt, ct);
        row.RevokedAt = Now;
        row.RevokedReason = "rotated";
        var nextHash = Hash(next.RefreshToken!);
        row.ReplacedById = await db.RefreshTokens.Where(x => x.TokenHash == nextHash).Select(x => (long?)x.Id).FirstAsync(ct);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        return next;
    }

    /// <summary>sign out of this device</summary>
    public async Task RevokeAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var hash = Hash(token);
        var row = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (row == null || row.RevokedAt != null) return;
        await RevokeFamilyAsync(row.FamilyId, "signout", ct);
    }

    /// <summary>end one sign-in (device) of this user</summary>
    public Task RevokeFamilyOfAsync(Guid userId, Guid family, string reason, CancellationToken ct)
    {
        var now = Now;
        return db.RefreshTokens.Where(x => x.UserId == userId && x.FamilyId == family && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    /// <summary>sign out everywhere: every refresh token revoked and every access token invalid at once (new security stamp). The caller saves.</summary>
    public async Task RevokeAllAsync(User u, string reason, CancellationToken ct)
    {
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        var now = Now;
        await db.RefreshTokens.Where(x => x.UserId == u.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    private Task RevokeFamilyAsync(Guid family, string reason, CancellationToken ct)
    {
        var now = Now;
        return db.RefreshTokens.Where(x => x.FamilyId == family && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    private static string? Clip(string? s, int max) => s == null ? null : s.Length > max ? s[..max] : s;
}

public record MembershipDto(string Owner, string Role);

/// <summary>GET /api/auth/me. Field names match what the account page reads. role: owner, or the member role (full | viewer) while working on someone else's account; account_role: customer | staff | admin.</summary>
public record MeDto(
    bool Success, Guid Id, string Email, string Name, string Phone, string Company, string? Avatar,
    [property: JsonPropertyName("has_password")] bool HasPassword,
    bool Twofa,
    string Since,
    [property: JsonPropertyName("last_login")] string? LastLogin,
    string Role,
    [property: JsonPropertyName("account_role")] string AccountRole,
    bool Staff,
    List<MembershipDto> Memberships,
    [property: JsonPropertyName("acting_as")] string? ActingAs,
    [property: JsonPropertyName("email_verified")] bool EmailVerified);

public record SessionDto(Guid Id, string Device, DateTimeOffset Created, DateTimeOffset LastUsed, string? Ip, bool Current,
    [property: JsonPropertyName("acting_as")] string? ActingAs);
