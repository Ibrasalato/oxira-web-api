using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Application.Features.Orders;
using Oxira.Web.Application.Features.Studio;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Portal;

// ───── shapes the account page reads (snake_case where it does) ─────
public record PortalSite([property: JsonPropertyName("order_id")] int OrderId, string Name, string Url, string Status, string Plan, string Domain,
    JsonNode Spec, JsonNode Settings, bool External, bool Verified, string Kind,
    [property: JsonPropertyName("domain_option")] string DomainOption, [property: JsonPropertyName("verify_txt")] string VerifyTxt);
public record PortalSub(int Id, [property: JsonPropertyName("order_id")] int OrderId, string Product, decimal Price, string Period, string Status,
    [property: JsonPropertyName("next_due")] string NextDue, [property: JsonPropertyName("invoice_url")] string InvoiceUrl);
public record PortalBooking(int Id, [property: JsonPropertyName("order_id")] int OrderId, string Name, string Phone, string Email, string Service, string Date, string Time, string Note, string Status);
public record PortalTicket(int Id, [property: JsonPropertyName("order_id")] int OrderId, string Code, string Type, [property: JsonPropertyName("type_name")] string TypeName,
    int Qty, decimal Amount, string Name, string Phone, string Email, string Status, [property: JsonPropertyName("checked_in")] string CheckedIn);
public record PortalContent([property: JsonPropertyName("order_id")] int OrderId, string Month, JsonNode Posts);
public record PortalView(bool Success, string Email, string Role, List<PortalSite> Sites, List<PortalSub> Subs, List<PortalBooking> Bookings, List<PortalTicket> Tickets, List<PortalContent> Content);

public record SubscribeInput([property: JsonPropertyName("order_id")] int? OrderId, string? Product, string? Lang);
public record CancelInput([property: JsonPropertyName("sub_id")] int? SubId);
public record BookingStatusInput([property: JsonPropertyName("booking_id")] int? BookingId, string? Status);
public record MenuInput([property: JsonPropertyName("order_id")] int? OrderId, JsonElement? Menu);
public record SettingsInput([property: JsonPropertyName("order_id")] int? OrderId, JsonElement? Settings);
public record CheckinInput(string? Code);

public record SubscribeResult(bool Success, string? PayUrl, string Status);
public record CheckinTicket(string Name, string Type, int Qty, string Code);
public record CheckinResult(bool Success, CheckinTicket Ticket);

public record LoadPortalQuery : IQuery<PortalView>;
public record SubscribeCommand(SubscribeInput Input) : ICommand<SubscribeResult>;
public record CancelSubscriptionCommand(int SubId) : ICommand<Done>;
public record SetBookingStatusCommand(BookingStatusInput Input) : ICommand<Done>;
public record SaveMenuCommand(MenuInput Input) : ICommand<Done>;
public record SaveSettingsCommand(SettingsInput Input) : ICommand<Done>;
public record CheckinCommand(string? Code) : ICommand<CheckinResult>;

/// <summary>The client portal: websites, add-on subscriptions, bookings, event tickets and monthly content of the account being worked on.</summary>
public class PortalHandlers(IAppDbContext db, TimeProvider clock, Access access, PaymentService payments, SitePublishing publishing, ISitePublisher kv,
    IOptions<PricingOptions> prices, IOptions<SiteOptions> site, ILogger<PortalHandlers> log)
    : IRequestHandler<LoadPortalQuery, PortalView>, IRequestHandler<SubscribeCommand, SubscribeResult>, IRequestHandler<CancelSubscriptionCommand, Done>,
      IRequestHandler<SetBookingStatusCommand, Done>, IRequestHandler<SaveMenuCommand, Done>, IRequestHandler<SaveSettingsCommand, Done>,
      IRequestHandler<CheckinCommand, CheckinResult>
{
    /// <summary>add-ons that work for sites hosted elsewhere (social and ads need no site at all)</summary>
    public static readonly string[] ForExternal = ["seo", "seogrowth", "social", "socialpro", "ads"];
    public static readonly string[] NoSiteNeeded = ["social", "socialpro", "ads"];
    private DateTimeOffset Now => clock.GetUtcNow();

    public static JsonNode ParseOr(string? json, Func<JsonNode> fallback)
    {
        try { return (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json)) ?? fallback(); } catch (JsonException) { return fallback(); }
    }

    /// <summary>the account's order (not cancelled); not found otherwise</summary>
    public static async Task<Order> OrderOfAsync(IAppDbContext db, Account a, int? orderId, CancellationToken ct) =>
        await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == a.Owner.Id && o.Status != OrderStatuses.Cancelled, ct)
        ?? throw DomainException.NotFound("الموقع غير موجود");

    public async Task<PortalView> Handle(LoadPortalQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var orders = await db.Orders.AsNoTracking().Where(o => o.UserId == a.Owner.Id && o.Status != OrderStatuses.Cancelled).OrderByDescending(o => o.Id).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var subs = await db.Subscriptions.AsNoTracking().Where(s => ids.Contains(s.OrderId) && s.Status != SubStatuses.Cancelled || ids.Contains(s.OrderId) && s.Status == SubStatuses.Cancelled && s.CancelledAt > Now.AddDays(-60))
            .OrderBy(s => s.Id).ToListAsync(ct);
        var bookings = await db.SiteBookings.AsNoTracking().Where(b => ids.Contains(b.OrderId)).OrderByDescending(b => b.Date).ThenByDescending(b => b.Time).Take(300).ToListAsync(ct);
        var tickets = await db.EventTickets.AsNoTracking().Where(t => ids.Contains(t.OrderId) && t.Status != "pending").OrderByDescending(t => t.Id).Take(500).ToListAsync(ct);
        var content = await db.ContentPacks.AsNoTracking().Where(c => ids.Contains(c.OrderId)).OrderByDescending(c => c.Month).Take(24).ToListAsync(ct);
        return new PortalView(true, a.Owner.Email, a.Role,
            orders.Select(o => new PortalSite(o.Id, o.SiteName, o.Url, o.Status, o.Plan, o.DomainName, ParseOr(o.SpecJson, () => new JsonObject()), ParseOr(o.SettingsJson, () => new JsonObject()),
                o.Kind == OrderKinds.External, o.Verified, o.Kind, o.DomainOption, o.Verified ? "" : o.VerifyToken)).ToList(),
            subs.Select(s => new PortalSub(s.Id, s.OrderId, s.Product, s.Price, s.Period, s.Status, s.NextDue?.ToString("yyyy-MM-dd") ?? "", s.InvoiceUrl)).ToList(),
            bookings.Select(b => new PortalBooking(b.Id, b.OrderId, b.Name, b.Phone, b.Email, b.Service, b.Date, b.Time, b.Note, b.Status)).ToList(),
            tickets.Select(t => new PortalTicket(t.Id, t.OrderId, t.Code, t.Type, t.TypeName, t.Qty, t.Amount, t.Name, t.Phone, t.Email, t.Status, t.CheckedInAt?.UtcDateTime.ToString("O") ?? "")).ToList(),
            content.Select(c => new PortalContent(c.OrderId, c.Month, ParseOr(c.PostsJson, () => new JsonArray()))).ToList());
    }

    public async Task<SubscribeResult> Handle(SubscribeCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var product = (cmd.Input.Product ?? "").Trim().ToLowerInvariant();
        if (!prices.Value.Addons.TryGetValue(product, out var price)) throw new DomainException("خدمة غير معروفة", "bad_request");
        await using var uow = await db.BeginAsync([$"order:{cmd.Input.OrderId}"], ct);
        var o = await OrderOfAsync(db, a, cmd.Input.OrderId, ct);
        if (o.Kind == OrderKinds.External)
        {
            if (!ForExternal.Contains(product)) throw new DomainException("هذه الخدمة للمواقع المستضافة لدى أوكسيرا", "not_for_external");
            if (!o.Verified && !NoSiteNeeded.Contains(product)) throw new DomainException("أكّد ملكية الموقع أولًا", "not_verified");
        }
        else if (o.PaidAt == null) throw new DomainException("الموقع لم يُنشر بعد", "not_live");

        var sub = await db.Subscriptions.Where(s => s.OrderId == o.Id && s.Product == product && s.Status != SubStatuses.Cancelled).OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        if (sub is { Status: SubStatuses.Active }) return new SubscribeResult(true, null, sub.Status);
        if (sub is { Status: SubStatuses.Cancelling })
        {
            sub.Status = SubStatuses.Active;
            sub.CancelledAt = null;
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
            await publishing.PushFeaturesAsync(o, ct);
            return new SubscribeResult(true, null, sub.Status);
        }
        if (sub == null)
        {
            sub = new Subscription { OrderId = o.Id, UserId = a.Owner.Id, Product = product, Price = price, Period = "month", Status = SubStatuses.Pending, CreatedAt = Now };
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync(ct);
        }
        if (price == 0)
        {
            sub.Status = SubStatuses.Active;
            await db.SaveChangesAsync(ct);
            await uow.CommitAsync(ct);
            await publishing.PushFeaturesAsync(o, ct);
            return new SubscribeResult(true, null, sub.Status);
        }
        var url = await Subscriptions.StartPaymentAsync(db, payments, site.Value, prices.Value, sub, o, a.Owner, Langs.Of(cmd.Input.Lang ?? a.Owner.Lang), Now, ct);
        await uow.CommitAsync(ct);
        return new SubscribeResult(true, url, sub.Status);
    }

    public async Task<Done> Handle(CancelSubscriptionCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == cmd.SubId, ct) ?? throw DomainException.NotFound();
        var o = await OrderOfAsync(db, a, sub.OrderId, ct);
        if (SitePublishing.Recurring.Contains(sub.Product)) throw new DomainException("الاستضافة والدومين يجددان مع الموقع", "bad_request");
        if (sub.Status == SubStatuses.Active) sub.Status = SubStatuses.Cancelling;
        else if (sub.Status is SubStatuses.Pending or SubStatuses.PastDue) { sub.Status = SubStatuses.Cancelled; sub.InvoiceUrl = ""; }
        sub.CancelledAt = Now;
        await db.SaveChangesAsync(ct);
        await publishing.PushFeaturesAsync(o, ct);
        return new Done();
    }

    public async Task<Done> Handle(SetBookingStatusCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        if (!BookingStatuses.All.Contains(cmd.Input.Status)) throw new DomainException("حالة غير معروفة", "bad_request");
        var b = await db.SiteBookings.FirstOrDefaultAsync(x => x.Id == cmd.Input.BookingId, ct) ?? throw DomainException.NotFound();
        await OrderOfAsync(db, a, b.OrderId, ct);
        b.Status = cmd.Input.Status!;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public async Task<Done> Handle(SaveMenuCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        if (cmd.Input.Menu is not { ValueKind: JsonValueKind.Object } m || m.GetRawText().Length > 100_000) throw new DomainException("القائمة غير صالحة", "bad_request");
        var o = await OrderOfAsync(db, a, cmd.Input.OrderId, ct);
        var spec = SpecJson.Parse(o.SpecJson);
        spec["menu"] = JsonNode.Parse(m.GetRawText());
        o.SpecJson = spec.ToJsonString(SpecJson.Relaxed);
        o.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (o.PaidAt != null && o.Kind == OrderKinds.Site) await publishing.PublishAsync(o, ct);
        return new Done();
    }

    private static readonly string[] SettingKeys =
        ["bookingFrom", "bookingTo", "slotMinutes", "capacity", "closedDays", "bookingNote", "googleReviewUrl", "chatNotes", "eventName", "tickets"];

    public async Task<Done> Handle(SaveSettingsCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        if (cmd.Input.Settings is not { ValueKind: JsonValueKind.Object } given || given.GetRawText().Length > 50_000) throw new DomainException("الإعدادات غير صالحة", "bad_request");
        var o = await OrderOfAsync(db, a, cmd.Input.OrderId, ct);
        var settings = ParseOr(o.SettingsJson, () => new JsonObject()) as JsonObject ?? [];
        foreach (var p in given.EnumerateObject().Where(p => SettingKeys.Contains(p.Name)))
            settings[p.Name] = JsonNode.Parse(p.Value.GetRawText());
        if (settings["googleReviewUrl"] is JsonValue g && g.TryGetValue<string>(out var gu) && gu != "" && !gu.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("رابط التقييم يبدأ بـ https://", "bad_request");
        o.SettingsJson = settings.ToJsonString(SpecJson.Relaxed);
        o.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        if (o.PaidAt != null) await publishing.PushFeaturesAsync(o, ct);
        return new Done();
    }

    public async Task<CheckinResult> Handle(CheckinCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var code = Line(cmd.Code, 16).ToUpperInvariant();
        var t = await (from x in db.EventTickets join o in db.Orders on x.OrderId equals o.Id where x.Code == code && o.UserId == a.Owner.Id select x).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("التذكرة غير موجودة", "not_found");
        if (t.Status == "used") throw new DomainException("التذكرة مستخدمة", "already_used", 409);
        if (t.Status != "paid") throw new DomainException("التذكرة غير مدفوعة", "not_paid", 409);
        t.Status = "used";
        t.CheckedInAt = Now;
        await db.SaveChangesAsync(ct);
        try { await kv.PutAsync($"ticket:{t.Code}", Tickets.KvJson(t, await EventNameAsync(t.OrderId, ct)), ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Ticket {Code} status not published", t.Code); }
        return new CheckinResult(true, new CheckinTicket(t.Name, t.TypeName != "" ? t.TypeName : t.Type, t.Qty, t.Code));
    }

    private async Task<string> EventNameAsync(int orderId, CancellationToken ct)
    {
        var json = await db.Orders.Where(o => o.Id == orderId).Select(o => o.SettingsJson).FirstOrDefaultAsync(ct);
        return ParseOr(json, () => new JsonObject())["eventName"]?.GetValue<string>() ?? "";
    }
}

public static class Tickets
{
    /// <summary>ticket:&lt;code&gt; as the worker's ticket page reads it: {code, type, name, qty, status, event}</summary>
    public static string KvJson(EventTicket t, string eventName) => new JsonObject
    {
        ["code"] = t.Code, ["type"] = t.TypeName != "" ? t.TypeName : t.Type, ["name"] = t.Name, ["qty"] = t.Qty, ["status"] = t.Status, ["event"] = eventName,
    }.ToJsonString(SpecJson.Relaxed);
}

/// <summary>Subscription payments: the first period and each renewal is a Moyasar invoice.</summary>
public static class Subscriptions
{
    public static async Task<string> StartPaymentAsync(IAppDbContext db, PaymentService payments, SiteOptions site, PricingOptions prices, Subscription sub, Order o, User owner,
        string lang, DateTimeOffset now, CancellationToken ct)
    {
        var ar = Langs.IsAr(lang);
        var vat = prices.Vat(sub.Price);
        var name = o.SiteName != "" ? o.SiteName : o.DomainName;
        var p = new Payment
        {
            Kind = PaymentKinds.Subscription, OrderId = o.Id, SubscriptionId = sub.Id, UserId = owner.Id, Email = owner.Email, Subtotal = sub.Price, Vat = vat,
            Amount = sub.Price + vat, CreatedAt = now,
            Description = ar ? $"اشتراك {sub.Product} ({(sub.Period == "year" ? "سنوي" : "شهري")}) · {name}" : $"{sub.Product} subscription ({sub.Period}ly) · {name}",
        };
        db.Payments.Add(p);
        await db.SaveChangesAsync(ct);
        var account = site.Account(lang);
        var url = await payments.StartAsync(p, PaymentService.WithQuery(account, "paid", sub.Id.ToString()) + "#billing", account + "#billing", ct);
        sub.PendingPaymentId = p.Id;
        sub.InvoiceUrl = url;
        await db.SaveChangesAsync(ct);
        return url;
    }

    public static DateOnly Next(DateOnly from, string period) => period == "year" ? from.AddYears(1) : from.AddMonths(1);
}

/// <summary>A paid subscription period: active until the next due date, the site's features updated, the owner notified.</summary>
public class SubscriptionPaidHandler(IAppDbContext db, TimeProvider clock, SitePublishing publishing, Notifier notifier, IEmailSender email, IOptions<SiteOptions> site)
    : INotificationHandler<PaymentPaidEvent>
{
    public async Task Handle(PaymentPaidEvent e, CancellationToken ct)
    {
        if (e.Kind != PaymentKinds.Subscription) return;
        var p = await db.Payments.AsNoTracking().FirstAsync(x => x.Id == e.PaymentId, ct);
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == p.SubscriptionId, ct);
        if (sub == null) return;
        var today = Riyadh.Today(clock);
        var from = sub.NextDue is { } due && due > today ? due : today;
        sub.NextDue = Subscriptions.Next(from, sub.Period);
        if (sub.Status != SubStatuses.Cancelling) sub.Status = SubStatuses.Active;
        sub.PendingPaymentId = null;
        sub.InvoiceUrl = "";
        await db.SaveChangesAsync(ct);
        var o = await db.Orders.FirstAsync(x => x.Id == sub.OrderId, ct);
        await publishing.PushFeaturesAsync(o, ct);
        if (o.UserId is { } owner)
        {
            await notifier.NotifyAsync(owner, NotificationTypes.Pay, product: sub.Product, site: o.SiteName != "" ? o.SiteName : o.DomainName, orderId: o.Id, ct: ct);
            var lang = await db.Users.Where(u => u.Id == owner).Select(u => u.Lang).FirstOrDefaultAsync(ct) ?? "ar";
            var number = await db.Invoices.Where(i => i.PaymentId == p.Id).Select(i => i.Number).FirstOrDefaultAsync(ct) ?? "";
            email.Enqueue(Templates.SubscriptionPaid(p.Email, sub, o, number, p.Amount, lang, site.Value));
        }
    }
}
