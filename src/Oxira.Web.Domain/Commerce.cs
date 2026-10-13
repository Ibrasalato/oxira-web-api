namespace Oxira.Web.Domain;

/// <summary>An uploaded image (logo, photo, avatar, ticket screenshot). Id: i_&lt;32 hex&gt;_j (JPEG) or _p (PNG).</summary>
public class Upload
{
    public string Id { get; set; } = "";
    public string Mime { get; set; } = "image/jpeg";
    public byte[] Content { get; set; } = [];
    public int Size { get; set; }
    public string SessionId { get; set; } = "";
    public Guid? UserId { get; set; }
    public string Ip { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A shareable studio preview (d_&lt;32 hex&gt;): the spec, served by the sites worker at /p/&lt;id&gt;/.</summary>
public class Draft
{
    public string Id { get; set; } = "";
    public string SpecJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class OrderKinds
{
    /// <summary>a website from Oxira Studio</summary>
    public const string Site = "site";
    /// <summary>"online in a day" bundle (fixed price)</summary>
    public const string Bundle = "bundle";
    /// <summary>a website hosted elsewhere, added to the account for SEO / social add-ons</summary>
    public const string External = "external";
}

public static class OrderStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Published = "published";
    public const string Cancelled = "cancelled";
    /// <summary>external sites: waiting for / done with ownership verification</summary>
    public const string External = "external";
}

/// <summary>An order: a studio website, the bundle, or an external site. The id is the public order number.</summary>
public class Order
{
    public int Id { get; set; }
    public string Kind { get; set; } = OrderKinds.Site;
    public Guid? UserId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ClientType { get; set; } = "";
    public string Template { get; set; } = "";
    public string SiteName { get; set; } = "";
    public string SiteLang { get; set; } = Langs.Default;
    /// <summary>new | own | none</summary>
    public string DomainOption { get; set; } = "none";
    public string DomainName { get; set; } = "";
    /// <summary>what the client chose, e.g. "plan:pro,domain"</summary>
    public string Addons { get; set; } = "";
    public string Plan { get; set; } = "";
    /// <summary>true when a new domain is part of what was paid (plan with domain, or the domain add-on)</summary>
    public bool DomainPaid { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public string SpecJson { get; set; } = "{}";
    /// <summary>add-on settings (booking hours, review link, chat notes, ticket types) as the worker reads them</summary>
    public string SettingsJson { get; set; } = "{}";
    /// <summary>bundle details, verification data, publishing data</summary>
    public string MetaJson { get; set; } = "{}";
    public string Status { get; set; } = OrderStatuses.Pending;
    public string Url { get; set; } = "";
    public string Slug { get; set; } = "";
    public bool Verified { get; set; }
    public string VerifyToken { get; set; } = "";
    /// <summary>partner referral code</summary>
    public string Ref { get; set; } = "";
    public string Lang { get; set; } = Langs.Default;
    public string Page { get; set; } = "";
    public string Ip { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public static class PaymentKinds
{
    public const string Order = "order";
    public const string Subscription = "subscription";
    public const string Ticket = "ticket";
}

public static class PaymentStatuses
{
    public const string Initiated = "initiated";
    public const string Paid = "paid";
    public const string Failed = "failed";
}

/// <summary>One card payment (a Moyasar hosted invoice). Marked paid only after the invoice is read back from Moyasar.</summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = PaymentKinds.Order;
    public int? OrderId { get; set; }
    public int? SubscriptionId { get; set; }
    public int? TicketId { get; set; }
    public Guid? UserId { get; set; }
    public string Email { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Vat { get; set; }
    /// <summary>charged amount (SAR, VAT included)</summary>
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string Provider { get; set; } = "moyasar";
    public string ProviderId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Status { get; set; } = PaymentStatuses.Initiated;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

/// <summary>A simplified tax invoice (ZATCA) for a paid payment.</summary>
public class Invoice
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public Guid PaymentId { get; set; }
    public Guid? UserId { get; set; }
    public string Email { get; set; } = "";
    public string Buyer { get; set; } = "";
    public int? OrderId { get; set; }
    /// <summary>add-on id for subscriptions (seo, booking …), empty for orders</summary>
    public string Product { get; set; } = "";
    /// <summary>order | subscription | ticket</summary>
    public string Kind { get; set; } = PaymentKinds.Order;
    public string Description { get; set; } = "";
    public string Site { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset PaidAt { get; set; }
}

public static class SubStatuses
{
    public const string Pending = "pending";
    public const string Active = "active";
    /// <summary>cancelled by the client, works until the paid period ends</summary>
    public const string Cancelling = "cancelling";
    public const string PastDue = "past_due";
    public const string Cancelled = "cancelled";
}

/// <summary>A recurring service on a site: an add-on (monthly), hosting or domain (yearly).</summary>
public class Subscription
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Guid? UserId { get; set; }
    public string Product { get; set; } = "";
    /// <summary>SAR before VAT, per period</summary>
    public decimal Price { get; set; }
    /// <summary>month | year</summary>
    public string Period { get; set; } = "month";
    public string Status { get; set; } = SubStatuses.Pending;
    public DateOnly? NextDue { get; set; }
    /// <summary>the open Moyasar invoice for the next period (or the first one)</summary>
    public Guid? PendingPaymentId { get; set; }
    public string InvoiceUrl { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}
