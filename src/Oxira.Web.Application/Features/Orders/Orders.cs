using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Application.Features.Studio;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Orders;

public record SiteOrderInput(string? Name, string? Phone, string? Email, string? Notes, string? ClientType, string? Template, string? SiteName, string? SiteLang,
    string? DomainOption, string? DomainName, string? Addons, string? Total, string? Spec, string? Lang, string? Page, string? Ref,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record BundleInput(string? Name, string? Business, string? Activity, string? City, string? Phone, string? Email, string? Whatsapp, string? Notes, string? Lang,
    string? ReturnUrl, [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record CheckoutInput(int? OrderId, string? ReturnUrl);
public record OrderIdInput(int? OrderId);

public record OrderCreated(bool Success, int OrderId, decimal Subtotal, decimal Vat, decimal Total);
public record BundleResult(bool Success, int Id, string? PayUrl);
public record CheckoutResult(bool Success, string PayUrl);
public record PaymentStatusResult(bool Success, string Status, int OrderId);

public record CreateSiteOrderCommand(SiteOrderInput Input) : ICommand<OrderCreated>;
public record CreateBundleOrderCommand(BundleInput Input) : ICommand<BundleResult>;
public record CheckoutCommand(CheckoutInput Input) : ICommand<CheckoutResult>;
public record PaymentStatusQuery(int OrderId) : IQuery<PaymentStatusResult>;
public record PaymentNotificationCommand(string Body) : ICommand<Done>;
public record ConfirmPaymentCommand(string ProviderId) : ICommand<bool>;

/// <summary>Prices are always computed here; the total the page sends is only compared, never charged.</summary>
public static class Pricing
{
    public record Quote(string Plan, bool DomainPaid, decimal Subtotal, decimal Vat, decimal Total);

    public static Quote ForSite(PricingOptions p, string? addons, string? domainOption)
    {
        var parts = (addons ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).ToList();
        var plan = parts.FirstOrDefault(x => x.StartsWith("plan:"))?[5..] ?? "pro";
        if (!p.Plans.ContainsKey(plan)) plan = "pro";
        var includesDomain = p.DomainPlans.Contains(plan);
        var newDomain = domainOption == "new";
        var extraDomain = newDomain && !includesDomain && parts.Contains("domain");
        var subtotal = p.Plans[plan] + (extraDomain ? p.Domain : 0);
        var vat = p.Vat(subtotal);
        return new Quote(plan, newDomain && (includesDomain || extraDomain), subtotal, vat, subtotal + vat);
    }
}

/// <summary>Writes a paid site to the sites store (slug:, host:, feat:) so the worker serves it.</summary>
public partial class SitePublishing(IAppDbContext db, ISitePublisher kv, TimeProvider clock, IOptions<SitesOptions> sites, ILogger<SitePublishing> log)
{
    public static readonly string[] Recurring = ["hosting", "domain"];

    public async Task PublishAsync(Order o, CancellationToken ct)
    {
        if (o.Slug == "") o.Slug = await UniqueSlugAsync(o, ct);
        var spec = SpecJson.Parse(o.SpecJson);
        spec["_order"] = o.Id;
        var raw = spec.ToJsonString(SpecJson.Relaxed);
        await kv.PutAsync($"slug:{o.Slug}", raw, ct);
        // host:<domain> only for a domain Oxira registers for this paid order, or the client's own domain once DNS proves ownership,
        // and never over a domain another site already holds
        if (o.DomainName != "" && (o.DomainOption == "new" && o.DomainPaid || o.DomainOption == "own" && o.Verified))
        {
            if (await Domains.InUseAsync(db, o.DomainName, o.Id, o.UserId, ct)) log.LogWarning("Order {Order}: {Domain} belongs to another site, host mapping skipped", o.Id, o.DomainName);
            else await kv.PutAsync($"host:{o.DomainName}", raw, ct);
        }
        await PushFeaturesAsync(o, ct);
        o.Url = $"https://{o.Slug}.{sites.Value.SubdomainBase}/";
        if (kv.Enabled)
        {
            o.Status = OrderStatuses.Published;
            o.PublishedAt ??= clock.GetUtcNow();
        }
        else log.LogInformation("Order {Order} paid; the sites store is not configured, so publishing is left to the team", o.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>feat:o&lt;id&gt; = {suspended, features, settings}: the add-ons the worker turns on for this site</summary>
    public async Task PushFeaturesAsync(Order o, CancellationToken ct)
    {
        var subs = await db.Subscriptions.AsNoTracking().Where(s => s.OrderId == o.Id).ToListAsync(ct);
        var features = subs.Where(s => s.Status is SubStatuses.Active or SubStatuses.Cancelling && !Recurring.Contains(s.Product)).Select(s => s.Product).Distinct().ToList();
        var hosting = subs.FirstOrDefault(s => s.Product == "hosting");
        var suspended = hosting is { Status: SubStatuses.Cancelled };
        JsonNode? settings;
        try { settings = JsonNode.Parse(o.SettingsJson); } catch (JsonException) { settings = new JsonObject(); }
        var feat = new JsonObject { ["suspended"] = suspended, ["features"] = new JsonArray(features.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()), ["settings"] = settings ?? new JsonObject() };
        await kv.PutAsync($"feat:o{o.Id}", feat.ToJsonString(SpecJson.Relaxed), ct);
    }

    private async Task<string> UniqueSlugAsync(Order o, CancellationToken ct)
    {
        var baseSlug = SlugRe().Replace((o.SiteName ?? "").ToLowerInvariant(), "-").Trim('-');
        if (baseSlug.Length > 40) baseSlug = baseSlug[..40].Trim('-');
        if (baseSlug.Length < 3 || baseSlug is "www" or "api" or "mail" or "admin") baseSlug = $"site{o.Id}";
        var slug = baseSlug;
        if (await db.Orders.AnyAsync(x => x.Slug == slug && x.Id != o.Id, ct)) slug = $"{baseSlug}-{o.Id}";
        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex SlugRe();
}

/// <summary>Studio orders, the bundle, checkout through Moyasar and the payment status the pages wait for.</summary>
public partial class OrderHandlers(IAppDbContext db, TimeProvider clock, Access access, ICurrentUser current, PaymentService payments, IEmailSender email,
    IOptions<PricingOptions> prices, IOptions<SiteOptions> site, IOptions<PaymentOptions> payOpt, ILogger<OrderHandlers> log)
    : IRequestHandler<CreateSiteOrderCommand, OrderCreated>, IRequestHandler<CreateBundleOrderCommand, BundleResult>, IRequestHandler<CheckoutCommand, CheckoutResult>,
      IRequestHandler<PaymentStatusQuery, PaymentStatusResult>, IRequestHandler<PaymentNotificationCommand, Done>, IRequestHandler<ConfirmPaymentCommand, bool>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<OrderCreated> Handle(CreateSiteOrderCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new OrderCreated(true, 0, 0, 0, 0);
        var name = Line(i.Name, 120);
        if (name == "" || Digits(i.Phone).Length < 7) throw new DomainException("أدخل الاسم ورقم الجوال", "invalid");
        var mail = Text.Email(i.Email);
        if (mail != "" && !IsEmail(mail)) throw new DomainException("البريد الإلكتروني غير صحيح", "email");
        var spec = SpecJson.Parse(i.Spec);
        spec.Remove("_order");
        var domainOption = i.DomainOption is "new" or "own" ? i.DomainOption : "none";
        var domain = domainOption == "none" ? "" : Domains.Normalize(i.DomainName);
        if (domainOption != "none" && domain == "" && !string.IsNullOrWhiteSpace(i.DomainName)) throw new DomainException("اسم الدومين غير صحيح", "domain");
        if (domain != "" && await Domains.InUseAsync(db, domain, null, null, ct)) throw new DomainException("هذا الدومين مستخدم لموقع آخر", "domain", 409);
        var q = Pricing.ForSite(prices.Value, i.Addons, domainOption);
        if (decimal.TryParse(i.Total, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var shown) && shown != q.Subtotal)
            log.LogWarning("Order total from the page ({Shown}) differs from the price ({Price})", shown, q.Subtotal);
        var me = await access.MaybeAsync(ct);
        var refCode = Line(i.Ref, 20).ToLowerInvariant();
        if (refCode != "" && !await db.Partners.AnyAsync(p => p.Code == refCode, ct)) refCode = "";
        var o = new Order
        {
            Kind = OrderKinds.Site, UserId = me?.Id, Email = mail != "" ? mail : me?.Email ?? "", Name = name, Phone = Line(i.Phone, 40), Notes = Clip(i.Notes, 2000),
            ClientType = Line(i.ClientType, 40), Template = Line(i.Template ?? SpecJson.Str(spec, "template"), 60), SiteName = Line(i.SiteName ?? SpecJson.Str(spec, "name"), 120),
            SiteLang = Langs.Of(i.SiteLang ?? SpecJson.Str(spec, "lang")), DomainOption = domainOption, DomainName = domain, Addons = Line(i.Addons, 200),
            Plan = q.Plan, DomainPaid = q.DomainPaid, Subtotal = q.Subtotal, Vat = q.Vat, Total = q.Total, SpecJson = spec.ToJsonString(SpecJson.Relaxed), Ref = refCode,
            VerifyToken = domainOption == "own" && domain != "" ? $"oxira-verify={Hex(12)}" : "",
            Lang = Langs.Of(i.Lang), Page = Line(i.Page, 300), Ip = current.Ip, CreatedAt = Now, UpdatedAt = Now,
        };
        db.Orders.Add(o);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.TeamOrder(o, site.Value.TeamEmail));
        if (IsEmail(o.Email)) email.Enqueue(Templates.OrderReceived(o));
        return new OrderCreated(true, o.Id, o.Subtotal, o.Vat, o.Total);
    }

    public async Task<BundleResult> Handle(CreateBundleOrderCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new BundleResult(true, 0, null);
        var mail = Text.Email(i.Email);
        var name = Line(i.Name, 80);
        var business = Line(i.Business, 100);
        if (name == "" || business == "" || Digits(i.Phone).Length < 7 || !IsEmail(mail)) throw new DomainException("أكمل البيانات المطلوبة", "invalid");
        var subtotal = prices.Value.Bundle;
        var vat = prices.Value.Vat(subtotal);
        var me = await access.MaybeAsync(ct);
        var o = new Order
        {
            Kind = OrderKinds.Bundle, UserId = me?.Id, Email = mail, Name = name, Phone = Line(i.Phone, 40), Notes = Clip(i.Notes, 1000), SiteName = business,
            Plan = "bundle", Subtotal = subtotal, Vat = vat, Total = subtotal + vat, Lang = Langs.Of(i.Lang), Ip = current.Ip, CreatedAt = Now, UpdatedAt = Now,
            MetaJson = new JsonObject { ["business"] = business, ["activity"] = Line(i.Activity, 80), ["city"] = Line(i.City, 60), ["whatsapp"] = Line(i.Whatsapp, 40) }.ToJsonString(SpecJson.Relaxed),
        };
        db.Orders.Add(o);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.TeamOrder(o, site.Value.TeamEmail));
        string? payUrl = null;
        try { payUrl = await StartOrderPaymentAsync(o, payments.SafeReturn(i.ReturnUrl, "/online-in-a-day/"), ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Bundle {Order}: payment page not available", o.Id); }
        if (payUrl == null) email.Enqueue(Templates.OrderReceived(o));
        return new BundleResult(true, o.Id, payUrl);
    }

    private async Task<string> StartOrderPaymentAsync(Order o, string returnUrl, CancellationToken ct)
    {
        var ar = Langs.IsAr(o.Lang);
        var p = new Payment
        {
            Kind = PaymentKinds.Order, OrderId = o.Id, UserId = o.UserId, Email = o.Email, Subtotal = o.Subtotal, Vat = o.Vat, Amount = o.Total, CreatedAt = Now,
            Description = o.Kind == OrderKinds.Bundle ? (ar ? $"باقة أونلاين في يوم · طلب {o.Id}" : $"Online in a day bundle · order {o.Id}")
                : (ar ? $"موقع {o.SiteName} · باقة {o.Plan} · طلب {o.Id}" : $"Website {o.SiteName} · {o.Plan} plan · order {o.Id}"),
        };
        db.Payments.Add(p);
        await db.SaveChangesAsync(ct);
        return await payments.StartAsync(p, PaymentService.WithQuery(returnUrl, "paid", o.Id.ToString()), PaymentService.WithQuery(returnUrl, "cancel", o.Id.ToString()), ct);
    }

    public async Task<CheckoutResult> Handle(CheckoutCommand cmd, CancellationToken ct)
    {
        var id = cmd.Input.OrderId ?? 0;
        await using var uow = await db.BeginAsync([$"order:{id}"], ct);
        var o = await db.Orders.FirstOrDefaultAsync(x => x.Id == id && x.Kind != OrderKinds.External, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        if (o.Status != OrderStatuses.Pending) throw new DomainException("تم دفع هذا الطلب", "paid", 409);
        // the same open payment page when the client comes back to pay
        var open = await db.Payments.Where(p => p.OrderId == o.Id && p.Kind == PaymentKinds.Order && p.Status == PaymentStatuses.Initiated && p.Url != "" && p.Amount == o.Total)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        string url;
        if (open != null && Now - open.CreatedAt < TimeSpan.FromHours(12)) url = open.Url;
        else
        {
            try { url = await StartOrderPaymentAsync(o, payments.SafeReturn(cmd.Input.ReturnUrl, "/studio/"), ct); }
            catch (Exception e) when (e is not OperationCanceledException and not DomainException)
            {
                log.LogError(e, "Checkout for order {Order} failed", o.Id);
                throw new DomainException("الدفع الإلكتروني غير متاح الآن، سيتواصل معك فريقنا برابط الدفع", "payment", 503);
            }
        }
        await uow.CommitAsync(ct);
        return new CheckoutResult(true, url);
    }

    /// <summary>paid as soon as the gateway says so (asked here too, so a missed webhook never leaves a client waiting)</summary>
    public async Task<PaymentStatusResult> Handle(PaymentStatusQuery q, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.OrderId, ct) ?? throw DomainException.NotFound("الطلب غير موجود");
        if (o.PaidAt != null) return new PaymentStatusResult(true, "paid", o.Id);
        var pending = await db.Payments.Where(p => p.OrderId == o.Id && p.Kind == PaymentKinds.Order && p.Status == PaymentStatuses.Initiated && p.ProviderId != "")
            .OrderByDescending(p => p.CreatedAt).Take(3).ToListAsync(ct);
        foreach (var p in pending)
            if (await payments.ConfirmAsync(p, ct)) return new PaymentStatusResult(true, "paid", o.Id);
        return new PaymentStatusResult(true, "pending", o.Id);
    }

    public async Task<Done> Handle(PaymentNotificationCommand cmd, CancellationToken ct)
    {
        JsonNode? body;
        try { body = JsonNode.Parse(cmd.Body); } catch (JsonException) { return new Done(); }
        await payments.HandleNotificationAsync(body, payOpt.Value.WebhookSecret, ct);
        return new Done();
    }

    public async Task<bool> Handle(ConfirmPaymentCommand cmd, CancellationToken ct)
    {
        var p = await db.Payments.FirstOrDefaultAsync(x => x.ProviderId == cmd.ProviderId, ct);
        return p != null && await payments.ConfirmAsync(p, ct);
    }
}

public static partial class Domains
{
    /// <summary>
    /// The domain already belongs to another site: a paid order, or a verified external site (of another account when owner is given).
    /// Unpaid orders and unverified external sites do not hold a domain, so nobody can squat one.
    /// </summary>
    public static Task<bool> InUseAsync(IAppDbContext db, string domain, int? exceptOrderId, Guid? owner, CancellationToken ct) =>
        db.Orders.AnyAsync(o => o.DomainName == domain && o.Id != (exceptOrderId ?? 0) && o.Status != OrderStatuses.Cancelled &&
            (o.Kind != OrderKinds.External && o.PaidAt != null || o.Kind == OrderKinds.External && o.Verified && (owner == null || o.UserId != owner)), ct);

    /// <summary>"https://www.Example.com/" → "example.com"; "" when it is not a domain name</summary>
    public static string Normalize(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        s = Regex.Replace(s, "^https?://", "");
        s = s.Split('/', '?', '#')[0].Trim('.');
        if (s.StartsWith("www.")) s = s[4..];
        return DomainRe().IsMatch(s) ? s : "";
    }

    [GeneratedRegex(@"^(?=.{4,253}$)([a-z0-9؀-ۿ]([a-z0-9؀-ۿ-]{0,61}[a-z0-9؀-ۿ])?\.)+[a-z؀-ۿ][a-z0-9؀-ۿ-]{1,62}$")] private static partial Regex DomainRe();
}

/// <summary>
/// A paid order: the account (created for a new email), hosting and domain subscriptions, publishing, emails, live updates.
/// Owner's rule: a client domain is never registered unless the client paid for it and the payment is confirmed — the team is
/// asked to register it only from here, after confirmation, and only when the order includes a paid domain.
/// </summary>
public class OrderPaidHandler(IAppDbContext db, TimeProvider clock, SitePublishing publishing, IEmailSender email, IRealtime realtime, IOutboundWebhook webhook,
    IOptions<PricingOptions> prices, IOptions<SiteOptions> site, ILogger<OrderPaidHandler> log) : INotificationHandler<PaymentPaidEvent>
{
    public async Task Handle(PaymentPaidEvent e, CancellationToken ct)
    {
        if (e.Kind != PaymentKinds.Order) return;
        var p = await db.Payments.AsNoTracking().FirstAsync(x => x.Id == e.PaymentId, ct);
        var o = await db.Orders.FirstOrDefaultAsync(x => x.Id == p.OrderId, ct);
        if (o == null || o.PaidAt != null) return;
        var now = clock.GetUtcNow();
        o.PaidAt = now;
        o.Status = OrderStatuses.Paid;
        o.UpdatedAt = now;
        var user = o.UserId is { } uid ? await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct) : await db.Users.FirstOrDefaultAsync(u => u.Email == o.Email, ct);
        if (user == null && IsEmail(o.Email))
        {
            // the account the order belongs to; the client sets a password with "forgot password" or signs up with this email
            user = new User { Email = o.Email, Name = o.Name, Phone = o.Phone, Lang = o.Lang, CreatedAt = now };
            db.Users.Add(user);
        }
        o.UserId ??= user?.Id;
        var nextYear = DateOnly.FromDateTime(Riyadh.Now(clock).DateTime).AddYears(1);
        if (!await db.Subscriptions.AnyAsync(s => s.OrderId == o.Id && s.Product == "hosting", ct))
            db.Subscriptions.Add(new Subscription { OrderId = o.Id, UserId = o.UserId, Product = "hosting", Price = prices.Value.HostingYearly, Period = "year", Status = SubStatuses.Active, NextDue = nextYear, CreatedAt = now });
        if (o.DomainPaid && !await db.Subscriptions.AnyAsync(s => s.OrderId == o.Id && s.Product == "domain", ct))
            db.Subscriptions.Add(new Subscription { OrderId = o.Id, UserId = o.UserId, Product = "domain", Price = prices.Value.DomainYearly, Period = "year", Status = SubStatuses.Active, NextDue = nextYear, CreatedAt = now });
        await db.SaveChangesAsync(ct);

        if (o.Kind == OrderKinds.Site)
        {
            try { await publishing.PublishAsync(o, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Publishing order {Order} failed", o.Id); }
        }
        var invoice = await db.Invoices.AsNoTracking().Where(i => i.PaymentId == p.Id).Select(i => i.Number).FirstOrDefaultAsync(ct) ?? "";
        email.Enqueue(Templates.TeamOrderPaid(o, invoice, site.Value.TeamEmail));
        if (IsEmail(o.Email)) email.Enqueue(Templates.OrderPaid(o, invoice, site.Value));
        var payload = new { orderId = o.Id, status = "paid", kind = o.Kind, url = o.Url };
        await realtime.ToOrderAsync(o.Id, "payment", payload);
        if (o.UserId is { } owner) await realtime.ToUserAsync(owner, "payment", payload);
        await realtime.ToStaffAsync("order", new { orderId = o.Id, status = "paid", total = o.Total, name = o.Name });
        webhook.Enqueue("order_paid", new { orderId = o.Id, o.Kind, o.Name, o.Email, o.Phone, o.SiteName, o.Plan, o.Total, o.DomainName, o.DomainPaid, o.Url, invoice });
    }
}
