using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Application.Features.Orders;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Application.Features.Seo;
using Oxira.Web.Application.Features.Studio;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Sites;

// ───── what the client sites post (plain HTML forms from the sites worker) ─────
public record SiteContactInput(string? Site, string? Lang, string? Name, string? Phone, string? Message, [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record SiteBookingInput(string? Site, string? Lang, string? Service, string? Date, string? Time, string? Name, string? Phone, string? Email, string? Note,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record SiteTicketInput(string? Site, string? Lang, string? Type, string? Name, string? Phone, string? Email, int? Qty,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record SiteReviewInput(string? Site, string? Lang, int? Rating, string? Comment, string? Name, [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record SiteChatInput(string? Site, string? SessionId, string? Message, string? Lang);

/// <summary>The answer to a form on a client site: a small page (title, text, link back) or a redirect.</summary>
public record SiteFormResult(bool Ok, string Lang, string Title, string Body, string BackUrl, string? Redirect = null);
public record SiteChatReply(string Reply);

public record SiteContactCommand(SiteContactInput Input) : ICommand<SiteFormResult>;
public record SiteBookingCommand(SiteBookingInput Input) : ICommand<SiteFormResult>;
public record SiteTicketCommand(SiteTicketInput Input) : ICommand<SiteFormResult>;
public record SiteReviewCommand(SiteReviewInput Input) : ICommand<SiteFormResult>;
public record SiteChatCommand(SiteChatInput Input) : ICommand<SiteChatReply>;

/// <summary>Forms and the AI assistant on the websites built in Oxira Studio (contact, booking, ticket purchase, review, chat).</summary>
public class ClientSiteHandlers(IAppDbContext db, TimeProvider clock, IQuota quota, ICurrentUser current, IAnthropic ai, ISitePublisher kv, PaymentService payments,
    SitePublishing publishing, Notifier notifier, IEmailSender email, IOptions<AiOptions> aiOpt, ILogger<ClientSiteHandlers> log)
    : IRequestHandler<SiteContactCommand, SiteFormResult>, IRequestHandler<SiteBookingCommand, SiteFormResult>, IRequestHandler<SiteTicketCommand, SiteFormResult>,
      IRequestHandler<SiteReviewCommand, SiteFormResult>, IRequestHandler<SiteChatCommand, SiteChatReply>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    private static (string Ok, string Fail) Words(string lang, string what) => Langs.IsAr(lang)
        ? what switch
        {
            "contact" => ("تم إرسال رسالتك", "تعذّر الإرسال"), "booking" => ("تم استلام حجزك", "تعذّر الحجز"),
            "ticket" => ("تم حجز تذكرتك", "تعذّر حجز التذكرة"), _ => ("شكرًا لتقييمك", "تعذّر الإرسال"),
        }
        : what switch
        {
            "contact" => ("Your message was sent", "Could not send"), "booking" => ("Your booking was received", "Booking failed"),
            "ticket" => ("Your ticket is booked", "Ticket booking failed"), _ => ("Thank you for your rating", "Could not send"),
        };

    private async Task<(Order Order, List<string> Features)> SiteAsync(string? site, CancellationToken ct)
    {
        var id = int.TryParse(site, out var i) ? i : 0;
        var o = await db.Orders.FirstOrDefaultAsync(x => x.Id == id && x.PaidAt != null && x.Kind != OrderKinds.External, ct) ?? throw DomainException.NotFound("الموقع غير موجود");
        var features = await db.Subscriptions.AsNoTracking().Where(s => s.OrderId == o.Id && (s.Status == SubStatuses.Active || s.Status == SubStatuses.Cancelling || s.Status == SubStatuses.PastDue))
            .Select(s => s.Product).ToListAsync(ct);
        return (o, features);
    }

    private SiteFormResult Fail(string lang, string what, string message, string back) => new(false, lang, Words(lang, what).Fail, message, back);
    private SiteFormResult Ok(string lang, string what, string message, string back, string? redirect = null) => new(true, lang, Words(lang, what).Ok, message, back, redirect);

    public async Task<SiteFormResult> Handle(SiteContactCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var (o, _) = await SiteAsync(i.Site, ct);
        var lang = Langs.Of(i.Lang ?? o.SiteLang);
        var ar = Langs.IsAr(lang);
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return Ok(lang, "contact", "", o.Url);
        var m = new SiteMessage { OrderId = o.Id, Name = Line(i.Name, 120), Phone = Line(i.Phone, 40), Message = Clip(i.Message, 2000), Lang = lang, Ip = current.Ip, CreatedAt = Now };
        if (m.Name == "" || m.Message == "" || !IsPhone(m.Phone)) return Fail(lang, "contact", ar ? "أكمل الاسم والجوال والرسالة" : "Please fill in your name, phone and message", o.Url);
        if (await quota.TryConsumeAsync($"site:contact:{o.Id}:{current.Ip}", 10, ct) == null) return Fail(lang, "contact", ar ? "حاول لاحقًا" : "Please try later", o.Url);
        db.SiteMessages.Add(m);
        await db.SaveChangesAsync(ct);
        if (IsEmail(o.Email)) email.Enqueue(Templates.SiteMessageToOwner(o, m));
        return Ok(lang, "contact", ar ? "سيتواصل معك فريقنا قريبًا." : "We will get back to you soon.", o.Url);
    }

    public async Task<SiteFormResult> Handle(SiteBookingCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var (o, features) = await SiteAsync(i.Site, ct);
        var lang = Langs.Of(i.Lang ?? o.SiteLang);
        var ar = Langs.IsAr(lang);
        if (!features.Contains("booking")) return Fail(lang, "booking", ar ? "الحجز غير متاح" : "Booking is not available", o.Url);
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return Ok(lang, "booking", "", o.Url);
        var settings = JsonText.Obj(o.SettingsJson);
        var name = Line(i.Name, 120);
        if (name == "" || !IsPhone(i.Phone)) return Fail(lang, "booking", ar ? "أكمل الاسم والجوال" : "Please fill in your name and phone", o.Url);
        if (!DateOnly.TryParseExact(Line(i.Date, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            !TimeOnly.TryParseExact(Line(i.Time, 5), "HH:mm", out var time))
            return Fail(lang, "booking", ar ? "اختر التاريخ والوقت" : "Choose a date and time", o.Url);
        var today = Riyadh.Today(clock);
        if (date < today || date > today.AddDays(120)) return Fail(lang, "booking", ar ? "اختر تاريخًا قادمًا" : "Choose an upcoming date", o.Url);
        var closed = (settings["closedDays"] as JsonArray ?? []).Select(x => int.TryParse(JsonText.Str(x), out var d) ? d : -1).ToHashSet();
        if (closed.Contains((int)date.DayOfWeek)) return Fail(lang, "booking", ar ? "هذا اليوم إجازة، اختر يومًا آخر" : "Closed on this day, choose another", o.Url);
        var capacity = int.TryParse(JsonText.Str(settings["capacity"]), out var c) && c > 0 ? c : 1;
        var d0 = date.ToString("yyyy-MM-dd");
        var t0 = time.ToString("HH:mm");
        await using var uow = await db.BeginAsync([$"booking:{o.Id}:{d0}:{t0}"], ct);
        if (await db.SiteBookings.CountAsync(b => b.OrderId == o.Id && b.Date == d0 && b.Time == t0 && b.Status != "cancelled", ct) >= capacity)
            return Fail(lang, "booking", ar ? "هذا الموعد محجوز، اختر وقتًا آخر" : "This time is taken, choose another", o.Url);
        var b = new SiteBooking
        {
            OrderId = o.Id, Name = name, Phone = Line(i.Phone, 40), Email = Text.Email(i.Email), Service = Line(i.Service, 160), Date = d0, Time = t0, Note = Clip(i.Note, 600),
            Lang = lang, CreatedAt = Now,
        };
        db.SiteBookings.Add(b);
        await db.SaveChangesAsync(ct);
        await uow.CommitAsync(ct);
        if (o.UserId is { } owner) await notifier.NotifyAsync(owner, NotificationTypes.Booking, site: o.SiteName, date: $"{d0} {t0}", subject: b.Name, orderId: o.Id, ct: ct);
        if (IsEmail(o.Email)) email.Enqueue(Templates.SiteBookingToOwner(o, b));
        if (IsEmail(b.Email)) email.Enqueue(Templates.SiteBookingToClient(o, b));
        return Ok(lang, "booking", ar ? $"موعدك {d0} الساعة {t0}. سيؤكده {o.SiteName} قريبًا." : $"Your appointment: {d0} at {t0}. {o.SiteName} will confirm it soon.", o.Url);
    }

    private static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    public async Task<SiteFormResult> Handle(SiteTicketCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var (o, features) = await SiteAsync(i.Site, ct);
        var lang = Langs.Of(i.Lang ?? o.SiteLang);
        var ar = Langs.IsAr(lang);
        if (!features.Contains("tickets")) return Fail(lang, "ticket", ar ? "التذاكر غير متاحة" : "Tickets are not available", o.Url);
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return Ok(lang, "ticket", "", o.Url);
        var settings = JsonText.Obj(o.SettingsJson);
        var type = (settings["tickets"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(t => JsonText.Str(t["id"]) == i.Type);
        if (type == null) return Fail(lang, "ticket", ar ? "نوع التذكرة غير موجود" : "Unknown ticket type", o.Url);
        var name = Line(i.Name, 120);
        var mail = Text.Email(i.Email);
        if (name == "" || !IsPhone(i.Phone) || !IsEmail(mail)) return Fail(lang, "ticket", ar ? "أكمل الاسم والجوال والبريد" : "Please fill in your name, phone and email", o.Url);
        var qty = Math.Clamp(i.Qty ?? 1, 1, 10);
        decimal price = decimal.TryParse(JsonText.Str(type["price"]), NumberStyles.Number, CultureInfo.InvariantCulture, out var pr) ? Math.Max(0, pr) : 0;
        var capacity = int.TryParse(JsonText.Str(type["capacity"]), out var cap) ? cap : 0;
        await using var uow = await db.BeginAsync([$"tickets:{o.Id}:{i.Type}"], ct);
        var recent = Now.AddMinutes(-30);
        var sold = await db.EventTickets.Where(t => t.OrderId == o.Id && t.Type == i.Type && (t.Status != "pending" || t.CreatedAt > recent)).SumAsync(t => (int?)t.Qty, ct) ?? 0;
        if (capacity > 0 && sold + qty > capacity) return Fail(lang, "ticket", ar ? "نفدت التذاكر" : "Sold out", o.Url);
        var ticket = new EventTicket
        {
            OrderId = o.Id, Code = NewCode(), Type = i.Type!, TypeName = Line(JsonText.Str(type["name"]), 80), Qty = qty, Amount = price * qty, Name = name,
            Phone = Line(i.Phone, 40), Email = mail, Lang = lang, Status = price == 0 ? "paid" : "pending", CreatedAt = Now,
        };
        db.EventTickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        var ticketUrl = $"{o.Url.TrimEnd('/')}/ticket/{ticket.Code}";
        if (price == 0)
        {
            await uow.CommitAsync(ct);
            await TicketIssuedAsync(o, ticket, kv, publishing, db, email, ct);
            return Ok(lang, "ticket", "", o.Url, ticketUrl);
        }
        var p = new Payment
        {
            Kind = PaymentKinds.Ticket, OrderId = o.Id, TicketId = ticket.Id, Email = mail, Subtotal = ticket.Amount, Vat = 0, Amount = ticket.Amount, CreatedAt = Now,
            Description = $"{ticket.TypeName} × {qty} · {(JsonText.Str(settings["eventName"]) is { Length: > 0 } ev ? ev : o.SiteName)}",
        };
        db.Payments.Add(p);
        await db.SaveChangesAsync(ct);
        try
        {
            var url = await payments.StartAsync(p, ticketUrl, o.Url, ct);
            await uow.CommitAsync(ct);
            return Ok(lang, "ticket", "", o.Url, url);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Ticket payment for order {Order} failed", o.Id);
            return Fail(lang, "ticket", ar ? "الدفع غير متاح الآن، حاول لاحقًا" : "Payment is not available right now", o.Url);
        }
    }

    /// <summary>a ticket that is paid (or free): published for the ticket page, counted, emailed</summary>
    public static async Task TicketIssuedAsync(Order o, EventTicket t, ISitePublisher kv, SitePublishing publishing, IAppDbContext db, IEmailSender email, CancellationToken ct)
    {
        var settings = JsonText.Obj(o.SettingsJson);
        await kv.PutAsync($"ticket:{t.Code}", Tickets.KvJson(t, JsonText.Str(settings["eventName"])), ct);
        var sold = settings["ticketsSold"] as JsonObject ?? [];
        sold[t.Type] = (int.TryParse(JsonText.Str(sold[t.Type]), out var n) ? n : 0) + t.Qty;
        settings["ticketsSold"] = sold;
        o.SettingsJson = settings.ToJsonString(SpecJson.Relaxed);
        await db.SaveChangesAsync(ct);
        await publishing.PushFeaturesAsync(o, ct);
        if (IsEmail(t.Email)) email.Enqueue(Templates.TicketIssued(o, t, $"{o.Url.TrimEnd('/')}/ticket/{t.Code}"));
    }

    public async Task<SiteFormResult> Handle(SiteReviewCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var (o, features) = await SiteAsync(i.Site, ct);
        var lang = Langs.Of(i.Lang ?? o.SiteLang);
        var ar = Langs.IsAr(lang);
        if (!features.Contains("reviews")) return Fail(lang, "review", ar ? "التقييم غير متاح" : "Reviews are not available", o.Url);
        var rating = Math.Clamp(i.Rating ?? 0, 0, 5);
        if (rating == 0) return Fail(lang, "review", ar ? "اختر عدد النجوم" : "Choose a rating", o.Url);
        if (string.IsNullOrEmpty(i.CompanyWebsite))
        {
            var r = new SiteReview { OrderId = o.Id, Rating = rating, Comment = Clip(i.Comment, 1500), Name = Line(i.Name, 80), CreatedAt = Now };
            db.SiteReviews.Add(r);
            await db.SaveChangesAsync(ct);
            if (rating <= 3 && IsEmail(o.Email)) email.Enqueue(Templates.SiteReviewToOwner(o, r));
        }
        var google = JsonText.Str(JsonText.Obj(o.SettingsJson)["googleReviewUrl"]);
        // happy customers go on to Google; the rest stay private feedback for the owner
        if (rating >= 4 && google.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return Ok(lang, "review", "", o.Url, google);
        return Ok(lang, "review", ar ? "وصل رأيك لصاحب النشاط." : "Your feedback reached the business.", o.Url);
    }

    public async Task<SiteChatReply> Handle(SiteChatCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var (o, features) = await SiteAsync(i.Site, ct);
        var lang = Langs.Of(i.Lang ?? o.SiteLang);
        var sorry = Langs.IsAr(lang) ? "المعذرة، لم أستطع الرد الآن. تواصل معنا مباشرة." : "Sorry, I can't answer right now. Please contact us directly.";
        if (!features.Contains("chat") || !ai.Enabled) return new SiteChatReply(sorry);
        var sid = SessionKey(i.SessionId);
        var text = Clip(i.Message, 1000);
        if (sid.Length < 6 || text == "") return new SiteChatReply("");
        if (await quota.TryConsumeAsync($"site:chat:{o.Id}", 400, ct) == null || await quota.TryConsumeAsync($"site:chat:s:{sid}", 40, ct) == null) return new SiteChatReply(sorry);
        var channel = $"site:{o.Id}";
        var history = await db.ChatMessages.AsNoTracking().Where(m => m.Channel == channel && m.SessionId == sid).OrderByDescending(m => m.Id).Take(10).ToListAsync(ct);
        history.Reverse();
        var messages = new JsonArray();
        foreach (var m in history.SkipWhile(m => m.Role != "user")) messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
        var notes = JsonText.Str(JsonText.Obj(o.SettingsJson)["chatNotes"]);
        var system = $"""
            You are the website assistant of "{o.SiteName}". Answer visitors briefly (1-5 lines) in their language, only from these facts; when you do not know,
            say so and suggest contacting the business. Never invent prices or promises.
            {Growth.SiteText(o)}
            Owner's notes: {Clip(notes, 3000)}
            """;
        try
        {
            var res = await ai.CreateMessageAsync(new AiRequest(system, messages, null, 500, aiOpt.Value.Model), ct);
            var reply = Clip(res.Text, 2000);
            db.ChatMessages.Add(new ChatMessage { SessionId = sid, Channel = channel, Role = "user", Content = text, Lang = lang, CreatedAt = Now });
            if (reply != "") db.ChatMessages.Add(new ChatMessage { SessionId = sid, Channel = channel, Role = "assistant", Content = reply, Lang = lang, CreatedAt = Now });
            await db.SaveChangesAsync(ct);
            return new SiteChatReply(reply == "" ? sorry : reply);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Site chat for order {Order} failed", o.Id);
            return new SiteChatReply(sorry);
        }
    }
}

/// <summary>A paid ticket: issued (ticket page, count, email) and the site owner notified.</summary>
public class TicketPaidHandler(IAppDbContext db, ISitePublisher kv, SitePublishing publishing, IEmailSender email) : INotificationHandler<PaymentPaidEvent>
{
    public async Task Handle(PaymentPaidEvent e, CancellationToken ct)
    {
        if (e.Kind != PaymentKinds.Ticket) return;
        var p = await db.Payments.AsNoTracking().FirstAsync(x => x.Id == e.PaymentId, ct);
        var t = await db.EventTickets.FirstOrDefaultAsync(x => x.Id == p.TicketId, ct);
        if (t == null || t.Status != "pending") return;
        t.Status = "paid";
        await db.SaveChangesAsync(ct);
        var o = await db.Orders.FirstAsync(x => x.Id == t.OrderId, ct);
        await ClientSiteHandlers.TicketIssuedAsync(o, t, kv, publishing, db, email, ct);
    }
}
