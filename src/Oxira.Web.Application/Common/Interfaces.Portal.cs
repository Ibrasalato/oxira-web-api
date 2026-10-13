using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Common;

public partial interface IAppDbContext
{
    DbSet<SiteBooking> SiteBookings { get; }
    DbSet<EventTicket> EventTickets { get; }
    DbSet<SiteReview> SiteReviews { get; }
    DbSet<SiteMessage> SiteMessages { get; }
    DbSet<ContentPack> ContentPacks { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<TicketMessage> TicketMessages { get; }
}

/// <summary>DNS TXT records (domain ownership checks for sites hosted elsewhere).</summary>
public interface IDnsTxt
{
    Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken ct = default);
}

/// <summary>Bell notifications: saved, then pushed live to the user's open pages ("notification" on /hubs/account).</summary>
public class Notifier(IAppDbContext db, IRealtime realtime, TimeProvider clock)
{
    public static readonly Dictionary<string, string> Sections = new()
    {
        [NotificationTypes.Pay] = "billing", [NotificationTypes.Renew] = "billing", [NotificationTypes.Social] = "social", [NotificationTypes.Seo] = "seo",
        [NotificationTypes.Booking] = "bookings", [NotificationTypes.Ticket] = "support", [NotificationTypes.TicketAdmin] = "support", [NotificationTypes.Meta] = "social",
    };

    public static object Dto(Notification n) => new
    {
        id = n.Id, type = n.Type, product = n.Product, site = n.Site, n = n.N, date = n.Date, subject = n.Subject, href = n.Href, order_id = n.OrderId, read = n.Read,
        at = n.CreatedAt,
    };

    public async Task NotifyAsync(Guid userId, string type, string product = "", string site = "", int? n = null, string date = "", string subject = "",
        int? orderId = null, int? ticketId = null, CancellationToken ct = default)
    {
        var item = new Notification
        {
            UserId = userId, Type = type, Product = product, Site = site, N = n, Date = date, Subject = subject.Length > 140 ? subject[..140] : subject,
            Href = Sections.GetValueOrDefault(type, "home"), OrderId = orderId, TicketId = ticketId, CreatedAt = clock.GetUtcNow(),
        };
        db.Notifications.Add(item);
        await db.SaveChangesAsync(ct);
        await realtime.ToUserAsync(userId, "notification", Dto(item));
    }

    /// <summary>one item for every Oxira team member</summary>
    public async Task NotifyStaffAsync(string type, string subject, int? ticketId = null, CancellationToken ct = default)
    {
        var staff = await db.Users.AsNoTracking().Where(u => Roles.Team.Contains(u.Role) && !u.Suspended).Select(u => u.Id).ToListAsync(ct);
        foreach (var id in staff) await NotifyAsync(id, type, subject: subject, ticketId: ticketId, ct: ct);
    }
}
