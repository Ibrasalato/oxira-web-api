using System.Text.Json.Nodes;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Features.Orders;

/// <summary>Published once a payment is confirmed (after commit). Handlers complete what was paid for: an order, a subscription, a ticket.</summary>
public record PaymentPaidEvent(Guid PaymentId, string Kind) : INotification;

/// <summary>
/// Card payments through the gateway (Moyasar). A payment is marked paid only after its invoice is read back from the gateway
/// with status "paid" and the expected amount, whatever the browser or the webhook said. Marking is idempotent (locked per payment).
/// </summary>
public class PaymentService(IAppDbContext db, TimeProvider clock, IPaymentGateway gateway, IPublisher events, IOptions<SiteOptions> site, IOptions<AuthOptions> auth,
    ILogger<PaymentService> log)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public string CallbackUrl => $"{site.Value.ApiUrl.TrimEnd('/')}/api/payments/moyasar/callback";

    /// <summary>a return page on an Oxira site (else the default)</summary>
    public string SafeReturn(string? url, string fallbackPath)
    {
        var u = (url ?? "").Trim();
        var origins = auth.Value.Origins.Append(site.Value.SiteUrl).Append("https://www.oxira.sa").Select(o => o.TrimEnd('/'));
        if (u.Length is > 0 and < 500 && Uri.TryCreate(u, UriKind.Absolute, out _) &&
            origins.Any(o => u.Equals(o, StringComparison.OrdinalIgnoreCase) || u.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase)))
            return u.Split('#')[0];
        return site.Value.SiteUrl.TrimEnd('/') + fallbackPath;
    }

    public static string WithQuery(string url, string key, string value) => $"{url}{(url.Contains('?') ? "&" : "?")}{key}={Uri.EscapeDataString(value)}";

    /// <summary>creates the gateway invoice for a saved payment and returns the page to send the client to</summary>
    public async Task<string> StartAsync(Payment p, string successUrl, string backUrl, CancellationToken ct)
    {
        var (id, url) = await gateway.CreateInvoiceAsync(new InvoiceRequest(p.Id, p.Amount, p.Description, CallbackUrl, successUrl, backUrl), ct);
        p.Provider = gateway.Name;
        p.ProviderId = id;
        p.Url = url;
        await db.SaveChangesAsync(ct);
        return url;
    }

    /// <summary>asks the gateway; marks the payment paid when the invoice is paid with the right amount</summary>
    public async Task<bool> ConfirmAsync(Payment p, CancellationToken ct)
    {
        if (p.Status == PaymentStatuses.Paid) return true;
        if (string.IsNullOrEmpty(p.ProviderId)) return false;
        GatewayInvoice? inv;
        try { inv = await gateway.GetInvoiceAsync(p.ProviderId, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Reading invoice {Invoice} failed", p.ProviderId); return false; }
        if (inv == null || inv.Status != "paid") return false;
        var expected = (long)Math.Round(p.Amount * 100, MidpointRounding.AwayFromZero);
        if (inv.AmountHalalas != expected || !string.Equals(inv.Currency, "SAR", StringComparison.OrdinalIgnoreCase))
        {
            log.LogError("Invoice {Invoice} paid {Amount} {Currency}, expected {Expected} SAR: not accepted", inv.Id, inv.AmountHalalas, inv.Currency, expected);
            return false;
        }
        return await MarkPaidAsync(p.Id, ct);
    }

    /// <summary>once per payment: status paid, a tax invoice, then PaymentPaidEvent</summary>
    private async Task<bool> MarkPaidAsync(Guid paymentId, CancellationToken ct)
    {
        string kind;
        await using (var uow = await db.BeginAsync([$"pay:{paymentId}"], ct))
        {
            var p = await db.Payments.FirstAsync(x => x.Id == paymentId, ct);
            if (p.Status == PaymentStatuses.Paid) return true;
            p.Status = PaymentStatuses.Paid;
            p.PaidAt = Now;
            var order = p.OrderId is { } oid ? await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == oid, ct) : null;
            var product = p.SubscriptionId is { } sid ? await db.Subscriptions.Where(s => s.Id == sid).Select(s => s.Product).FirstOrDefaultAsync(ct) ?? "" : "";
            var inv = new Invoice
            {
                PaymentId = p.Id, UserId = p.UserId ?? order?.UserId, Email = p.Email, Buyer = order?.Name ?? "", OrderId = p.OrderId, Product = product, Kind = p.Kind,
                Description = p.Description, Site = order == null ? "" : order.SiteName != "" ? order.SiteName : order.DomainName,
                Subtotal = p.Subtotal, Vat = p.Vat, Total = p.Amount, PaidAt = p.PaidAt.Value,
            };
            // ticket sales are the site owner's sales, not Oxira's: no Oxira tax invoice
            if (p.Kind != PaymentKinds.Ticket)
            {
                db.Invoices.Add(inv);
                await db.SaveChangesAsync(ct);
                inv.Number = $"OX-{Riyadh.Now(clock):yyyy}-{inv.Id:D6}";
            }
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
            kind = p.Kind;
        }
        await events.Publish(new PaymentPaidEvent(paymentId, kind), ct);
        return true;
    }

    /// <summary>gateway notification (webhook or callback_url): finds our payment and confirms it with the gateway</summary>
    public async Task<bool> HandleNotificationAsync(JsonNode? body, string webhookSecret, CancellationToken ct)
    {
        if (body is not JsonObject o) return false;
        if (webhookSecret != "" && (string?)o["secret_token"] is { } token && token != webhookSecret) return false;
        var ids = new[] { o["id"], o["invoice_id"], o["data"]?["invoice_id"], o["data"]?["id"], o["invoice"]?["id"] }
            .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var metaId = o["metadata"]?["payment_id"] ?? o["data"]?["metadata"]?["payment_id"];
        Payment? p = null;
        if (ids.Count > 0) p = await db.Payments.FirstOrDefaultAsync(x => ids.Contains(x.ProviderId), ct);
        if (p == null && metaId is JsonValue mv && mv.TryGetValue<string>(out var ms) && Guid.TryParse(ms, out var pid))
            p = await db.Payments.FirstOrDefaultAsync(x => x.Id == pid, ct);
        return p != null && await ConfirmAsync(p, ct);
    }
}
