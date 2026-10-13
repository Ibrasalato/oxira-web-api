using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Common;

public partial interface IAppDbContext
{
    DbSet<Upload> Uploads { get; }
    DbSet<Draft> Drafts { get; }
    DbSet<Order> Orders { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Subscription> Subscriptions { get; }
}

// ───── payments ─────
public record InvoiceRequest(Guid PaymentId, decimal Amount, string Description, string CallbackUrl, string SuccessUrl, string BackUrl);
public record GatewayInvoice(string Id, string Status, long AmountHalalas, string Currency, string? PaymentId);

/// <summary>Hosted card checkout (Moyasar invoices: mada, Visa, Mastercard, Apple Pay).</summary>
public interface IPaymentGateway
{
    /// <summary>"moyasar" or "simulated"</summary>
    string Name { get; }
    /// <returns>the provider's invoice id and the page to send the client to</returns>
    Task<(string Id, string Url)> CreateInvoiceAsync(InvoiceRequest request, CancellationToken ct);
    /// <summary>reads the invoice back from the provider (the only proof of payment that is trusted)</summary>
    Task<GatewayInvoice?> GetInvoiceAsync(string id, CancellationToken ct);
}

// ───── publishing client sites (Cloudflare KV, read by the sites worker) ─────
/// <summary>
/// Keys: host:&lt;domain&gt;, slug:&lt;slug&gt;, draft:&lt;id&gt;, img:&lt;id&gt;, feat:o&lt;order&gt;, seo:o&lt;order&gt;, post:o&lt;order&gt;:&lt;slug&gt;,
/// ticket:&lt;code&gt;, hits:o&lt;order&gt;:&lt;yyyy-mm&gt;. Without Cloudflare settings the keys are only logged.
/// </summary>
public interface ISitePublisher
{
    bool Enabled { get; }
    Task PutAsync(string key, string value, CancellationToken ct = default);
    Task PutBytesAsync(string key, byte[] value, string contentType, CancellationToken ct = default);
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>"Prices" section (SAR before VAT). Keep in sync with src/i18n/builder.ts and src/builder/addons.ts in the website.</summary>
public class PricingOptions
{
    public decimal VatRate { get; set; } = 0.15m;
    public Dictionary<string, decimal> Plans { get; set; } = new() { ["starter"] = 1499, ["pro"] = 2499, ["business"] = 4999 };
    /// <summary>plans that include a new domain</summary>
    public string[] PlansWithDomain { get; set; } = [];
    public static readonly string[] DefaultPlansWithDomain = ["pro", "business"];
    public string[] DomainPlans => PlansWithDomain.Length > 0 ? PlansWithDomain : DefaultPlansWithDomain;
    public decimal Domain { get; set; } = 99;
    public decimal Bundle { get; set; } = 3999;
    /// <summary>yearly renewals after the first year (the first year is part of the order)</summary>
    public decimal HostingYearly { get; set; } = 499;
    public decimal DomainYearly { get; set; } = 99;
    public Dictionary<string, decimal> Addons { get; set; } = new()
    {
        ["booking"] = 99, ["menu"] = 49, ["reviews"] = 79, ["chat"] = 199, ["content"] = 499, ["tickets"] = 0,
        ["seo"] = 299, ["seogrowth"] = 799, ["social"] = 699, ["socialpro"] = 1499, ["ads"] = 999,
    };
    /// <summary>ticket sales fee (percent of each paid ticket)</summary>
    public decimal TicketFeePercent { get; set; } = 5;

    public decimal Vat(decimal subtotal) => Math.Round(subtotal * VatRate, 2, MidpointRounding.AwayFromZero);
}

/// <summary>"Payments" section.</summary>
public class PaymentOptions
{
    /// <summary>Moyasar secret key (sk_live_… / sk_test_…), from an environment variable</summary>
    public string MoyasarSecretKey { get; set; } = "";
    public string MoyasarUrl { get; set; } = "https://api.moyasar.com";
    /// <summary>development and tests: a local payment page instead of Moyasar (the live account is not activated yet)</summary>
    public bool Simulate { get; set; }
    /// <summary>optional Moyasar webhook secret_token; the invoice is still read back from Moyasar before anything is marked paid</summary>
    public string WebhookSecret { get; set; } = "";
}

/// <summary>"Sites" section: where client sites and previews live.</summary>
public class SitesOptions
{
    /// <summary>the sites worker's own address (previews at /p/&lt;id&gt;/, images at /_img/&lt;id&gt;)</summary>
    public string WorkerUrl { get; set; } = "https://oxira-sites.oxira-sa.workers.dev";
    /// <summary>free subdomains: https://&lt;slug&gt;.oxira.sa/</summary>
    public string SubdomainBase { get; set; } = "oxira.sa";
}
