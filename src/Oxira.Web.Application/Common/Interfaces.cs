using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Common;

/// <summary>The database as the application sees it (EF Core + SQL Server in Infrastructure).</summary>
public partial interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<EmailCode> EmailCodes { get; }
    DbSet<MagicLink> MagicLinks { get; }
    DbSet<TeamMember> TeamMembers { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Opens a transaction and takes exclusive application locks on the given keys (e.g. "rt:{hash}", "order:{id}"),
    /// so two requests on the same thing never interleave. Joins an outer transaction when one is open.
    /// </summary>
    Task<IUnitOfWork> BeginAsync(IEnumerable<string> locks, CancellationToken ct = default);
}

public interface IUnitOfWork : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>The caller: the JWT (Oxira account) and network details.</summary>
public interface ICurrentUser
{
    Guid? Id { get; }
    /// <summary>security stamp in the access token</summary>
    string? Stamp { get; }
    /// <summary>the sign-in (refresh token family) this access token belongs to</summary>
    Guid? SessionId { get; }
    /// <summary>the team owner whose account this session works on (account switch)</summary>
    Guid? ActingOwnerId { get; }
    string Ip { get; }
    string? UserAgent { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string? hash, string password);
}

public record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenIssuer
{
    /// <summary>short-lived signed JWT (sub, email, name, role, stamp, sid, jti, iat; act when switched)</summary>
    AccessToken Issue(User user, Guid sessionId, Guid? actingOwnerId);
    TimeSpan RefreshLifetime { get; }
}

/// <summary>Atomic daily counters (SQL MERGE with HOLDLOCK), keyed by name and Riyadh day.</summary>
public interface IQuota
{
    /// <returns>the count after this use, or null when the limit was already reached</returns>
    Task<int?> TryConsumeAsync(string key, int limit, CancellationToken ct = default);
    Task RefundAsync(string key, CancellationToken ct = default);
    Task<int> UsedAsync(string key, CancellationToken ct = default);
}

// ───── email ─────
public record EmailAttachment(string Name, string Mime, byte[] Content, string? ContentId = null);
public record EmailMessage(IReadOnlyList<string> To, string Subject, string Html, string? ReplyTo = null, IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Queues an email; delivery happens in the background (SMTP, Zoho Mail).</summary>
public interface IEmailSender
{
    void Enqueue(EmailMessage message);
}

// ───── live updates ─────
/// <summary>
/// SignalR pushes. /hubs/account (signed in): "notification", "ticket", "payment" to the user; staff also get team events.
/// /hubs/orders (anyone with the order number): "payment" for one order. Failures are logged, never thrown.
/// </summary>
public interface IRealtime
{
    Task ToUserAsync(Guid userId, string evt, object payload);
    Task ToStaffAsync(string evt, object payload);
    Task ToOrderAsync(int orderId, string evt, object payload);
}

/// <summary>Optional copy of team events to an outside webhook (Site:NotifyWebhook, e.g. an n8n workflow). Fire-and-forget; the API never depends on it.</summary>
public interface IOutboundWebhook
{
    void Enqueue(string evt, object data);
}
