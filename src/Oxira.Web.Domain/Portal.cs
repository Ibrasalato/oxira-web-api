namespace Oxira.Web.Domain;

/// <summary>A booking made on a client's site (booking add-on).</summary>
public class SiteBooking
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Service { get; set; } = "";
    /// <summary>yyyy-MM-dd and HH:mm, the site's local time</summary>
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>new | confirmed | done | cancelled</summary>
    public string Status { get; set; } = "new";
    public string Lang { get; set; } = Langs.Default;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class BookingStatuses
{
    public static readonly string[] All = ["new", "confirmed", "done", "cancelled"];
}

/// <summary>Event tickets sold on a client's site (tickets add-on). The code is printed as a QR on the ticket page.</summary>
public class EventTicket
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Code { get; set; } = "";
    /// <summary>ticket type id from the site's settings</summary>
    public string Type { get; set; } = "";
    public string TypeName { get; set; } = "";
    public int Qty { get; set; } = 1;
    /// <summary>what the buyer pays (SAR, VAT included); 0 for free tickets</summary>
    public decimal Amount { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>pending (not paid yet) | paid | used</summary>
    public string Status { get; set; } = "pending";
    public string Lang { get; set; } = Langs.Default;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
}

/// <summary>A rating left on a client's site (/review).</summary>
public class SiteReview
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A message from a client site's contact form (emailed to the site owner).</summary>
public class SiteMessage
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Message { get; set; } = "";
    public string Lang { get; set; } = Langs.Default;
    public string Ip { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The monthly content add-on: a month of ready posts for a site.</summary>
public class ContentPack
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = "";
    /// <summary>[{day, title, caption, hashtags, idea}]</summary>
    public string PostsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}

public static class NotificationTypes
{
    public const string Pay = "pay";
    public const string Renew = "renew";
    public const string Social = "social";
    public const string Seo = "seo";
    public const string Booking = "booking";
    public const string Ticket = "ticket";
    public const string TicketAdmin = "ticket_admin";
    public const string Meta = "meta";
}

/// <summary>A bell item in the site header (and pushed live).</summary>
public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Product { get; set; } = "";
    public string Site { get; set; } = "";
    public int? N { get; set; }
    public string Date { get; set; } = "";
    public string Subject { get; set; } = "";
    /// <summary>the account page section (#billing, #support, #seo …)</summary>
    public string Href { get; set; } = "";
    public int? OrderId { get; set; }
    /// <summary>support ticket the item is about</summary>
    public int? TicketId { get; set; }
    public bool Read { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class TicketStatuses
{
    public const string Open = "open";
    public const string Answered = "answered";
    public const string Closed = "closed";
}

/// <summary>A support ticket from a client account. The Oxira team sees all of them.</summary>
public class SupportTicket
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    /// <summary>the account (owner) the ticket belongs to</summary>
    public Guid OwnerId { get; set; }
    public Guid CreatedById { get; set; }
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    /// <summary>website | domain | seo | social | ads | billing | other</summary>
    public string Category { get; set; } = "other";
    public int? OrderId { get; set; }
    public string Status { get; set; } = TicketStatuses.Open;
    public bool UnreadByOwner { get; set; }
    public bool UnreadByStaff { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TicketMessage> Messages { get; set; } = [];
}

public static class TicketCategories
{
    public static readonly string[] All = ["website", "domain", "seo", "social", "ads", "billing", "other"];
}

public class TicketMessage
{
    public long Id { get; set; }
    public int TicketId { get; set; }
    /// <summary>client | oxira</summary>
    public string From { get; set; } = "client";
    public string By { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>uploaded image ids (JSON array)</summary>
    public string ImagesJson { get; set; } = "[]";
    public DateTimeOffset At { get; set; }
}
