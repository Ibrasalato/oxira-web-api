using System.Net;
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
using Oxira.Web.Application.Features.Audit;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Agent;

public record TryAgentInput(string? Url, string? Business, string? Notes, string? Email, string? Phone, string? Lang,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record DemoTurn(string? Role, string? Content);
public record DemoChatInput(string? DemoId, string? Message, List<DemoTurn>? History);
public record AgentSubscribeInput(string? DemoId, string? Plan, string? Name, string? Business, string? Email, string? Phone, string? Whatsapp, string? City,
    string? Notes, string? Lang, [property: JsonPropertyName("company_website")] string? CompanyWebsite);

public record TryAgentResult(bool Success, string DemoId, string Business);
public record DemoDto(bool Success, string Business, bool Example, string Welcome, List<string> Suggestions);
public record DemoReply(bool Success, string Reply, int Left);

public record TryAgentCommand(TryAgentInput Input) : ICommand<TryAgentResult>;
public record GetDemoQuery(string? Id) : IQuery<DemoDto>;
public record DemoChatCommand(DemoChatInput Input) : ICommand<DemoReply>;
public record AgentSubscribeCommand(AgentSubscribeInput Input) : ICommand<Features.Public.SubmitResult>;

/// <summary>The fictional, clearly labelled example businesses on /whatsapp-agent/ (ids fixed in the website's wa-examples.json).</summary>
public static class AgentExamples
{
    public record Example(string Id, string Business, string Lang, string Profile, string Welcome, string[] Suggestions);

    private const string Rules = """
        Rules: you are a demo of a WhatsApp customer-service agent built by Oxira for a FICTIONAL example business. Answer only from the facts above;
        when you do not know (exact prices for special cases, availability), say you will check with the team and offer to hand over to a staff member.
        Keep replies short like WhatsApp messages (1-6 lines), use *bold* for prices, a friendly emoji at most once, and always end with a helpful next step
        (book, order, or hand over). Never mention that you are Claude or an AI model unless asked; say you are the business's assistant.
        """;

    public static readonly Example[] All =
    [
        new("897fa1eda859", "مطعم ليوان الطيب", "ar",
            """
            You are the WhatsApp assistant of "مطعم ليوان الطيب", a Saudi restaurant in Riyadh. Speak Saudi Arabic.
            Menu: كبسة لحم حاشي 65 ريال، كبسة دجاج 38 ريال، مندي لحم 70 ريال، مشاوي مشكلة 85 ريال، كنافة بالقشطة 22 ريال، شاي/قهوة 8 ريال.
            Delivery inside Riyadh (including النرجس، الملقا، الياسمين) 15 ريال, free for orders over 150 ريال. Open daily 1 pm - 1 am. Payment: mada, Apple Pay, cash.
            Take orders: items, quantity, name, phone, delivery district; confirm the total.
            """ + Rules,
            "هلا والله 👋 أنا مساعد مطعم ليوان الطيب. تبي تطلب، أو تسأل عن المنيو والتوصيل؟",
            ["كم سعر الكبسة؟", "توصلون حي النرجس؟", "ابي اطلب 2 كبسة لحم"]),
        new("5c57e9c3dae0", "عيادة لؤلؤة الابتسامة للأسنان", "ar",
            """
            You are the WhatsApp assistant of "عيادة لؤلؤة الابتسامة للأسنان", a dental clinic in Jeddah. Speak Saudi Arabic, politely.
            Prices: تنظيف 250 ريال، تبييض بالليزر 1200 ريال، حشوة تجميلية من 350 ريال، كشف 150 ريال (يُخصم عند العلاج). التقويم الشفاف: السعر بعد الكشف.
            Insurance: Bupa, Tawuniya, MedGulf (coverage depends on the policy). Female dentist on Sunday and Tuesday. Hours: Sat-Thu 10 am - 10 pm.
            Book appointments: service, preferred day/time, name, phone.
            """ + Rules,
            "أهلاً وسهلاً 😊 معك مساعد عيادة لؤلؤة الابتسامة. كيف أقدر أخدمك؟ أسعار، تأمين، أو حجز موعد؟",
            ["كم سعر تبييض الأسنان؟", "تقبلون تأمين بوبا؟", "ابي احجز موعد كشف"]),
        new("d8dbb8bf0917", "صالون نوّارة للسيدات", "ar",
            """
            You are the WhatsApp assistant of "صالون نوّارة للسيدات", a ladies' salon in Cairo. Speak Egyptian Arabic.
            Prices (EGP): بروتين يبدأ من 2500 جنيه حسب طول الشعر، صبغة من 1200، قص وسشوار 450، مانيكير وباديكير 400، ميكب سهرة 2000، ميكب عروسة مع التسريحة يبدأ من 6000 وعربون 1000 لتثبيت الموعد.
            Payment: InstaPay, cash, Visa. Hours: daily 11 am - 10 pm except Friday from 4 pm. Book: service, day/time, name, phone.
            """ + Rules,
            "مساء الفل 🌸 أنا مساعدة صالون نوّارة. تحبي تعرفي الأسعار ولا أحجزلك موعد؟",
            ["بكام البروتين؟", "ميكب العروسة بكام؟", "عايزة أحجز موعد"]),
        new("b9baf1b5a46f", "عطور مِسك الدار", "en",
            """
            You are the WhatsApp assistant of "Misk Al Dar Perfumes" (عطور مِسك الدار), a Saudi online perfume store. Reply in the customer's language (English or Arabic).
            Products (SAR): Oud Royal 50 ml 289, Musk Rose 50 ml 189, Amber Night 100 ml 249, Gift set (perfume, incense, electric incense burner) 359, incense 120.
            Shipping: inside Saudi Arabia 2-4 days, 25 SAR (free over 300); Gulf countries incl. UAE 5-7 days, 60 SAR. Free gift wrapping with a card.
            Returns within 14 days if unopened. Take orders: product, quantity, name, phone, city and address.
            """ + Rules,
            "Hi! 🌹 I'm the Misk Al Dar assistant. Looking for a perfume, a gift, or shipping info?",
            ["Do you ship to Dubai?", "How much is the gift set?", "Can you gift wrap it?"]),
    ];
}

/// <summary>
/// WhatsApp agent demos: reads the business's website (SSRF-safe), asks Claude to write the agent's instructions, then lets the visitor chat with it.
/// Each demo has a message cap; example demos are capped per visitor per day.
/// </summary>
public partial class AgentHandlers(IAppDbContext db, TimeProvider clock, IAnthropic ai, ISafeFetcher fetcher, IQuota quota, ICurrentUser current, IEmailSender email,
    IOptions<AgentOptions> opt, IOptions<AiOptions> aiOpt, IOptions<SiteOptions> site, ILogger<AgentHandlers> log)
    : IRequestHandler<TryAgentCommand, TryAgentResult>, IRequestHandler<GetDemoQuery, DemoDto>, IRequestHandler<DemoChatCommand, DemoReply>,
      IRequestHandler<AgentSubscribeCommand, Features.Public.SubmitResult>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<TryAgentResult> Handle(TryAgentCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var business = Line(i.Business, 100);
        var notes = Clip(i.Notes, 1500);
        Uri? url = null;
        if (!string.IsNullOrWhiteSpace(i.Url)) url = UrlInput.Parse(i.Url) ?? throw new DomainException("رابط الموقع غير صحيح", "url");
        if (business == "" || url == null && notes == "") throw new DomainException("اكتب اسم النشاط ورابط الموقع أو نبذة عنه", "input");
        var mail = Text.Email(i.Email);
        if (!IsEmail(mail)) throw new DomainException("البريد الإلكتروني غير صحيح", "email");
        var lang = Langs.Of(i.Lang);
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new TryAgentResult(true, Hex(6), business);
        if (!ai.Enabled) throw new DomainException("الخدمة غير متاحة الآن", "server", 503);
        var o = opt.Value;
        if (await quota.TryConsumeAsync($"agent:ip:{current.Ip}", o.DemosPerIp, ct) == null || await quota.TryConsumeAsync($"agent:mail:{mail}", o.DemosPerEmail, ct) == null ||
            await quota.TryConsumeAsync("agent:all", o.DemosPerDay, ct) == null)
            throw DomainException.Limit("وصلت للحد اليومي للتجارب، حاول غدًا");

        var siteText = "";
        if (url != null)
        {
            var page = await fetcher.GetAsync(url, 2_000_000, ct);
            if (!page.Ok && notes == "") throw new DomainException("تعذّر قراءة الموقع، اكتب نبذة عن نشاطك بدلًا منه", "fetch", 422);
            siteText = page.Ok ? PageText(page.Body, 9000) : "";
            if (siteText.Length < 200 && notes.Length < 40) throw new DomainException("الموقع لا يحوي نصًا كافيًا، اكتب نبذة عن نشاطك", "content", 422);
        }

        JsonObject profile;
        try { profile = await BuildProfileAsync(business, url, notes, siteText, lang, ct); }
        catch (Exception e) when (e is not OperationCanceledException and not DomainException)
        {
            log.LogError(e, "Agent profile failed for {Business}", business);
            throw new DomainException("تعذّر تجهيز الوكيل، حاول مرة أخرى", "server", 503);
        }
        var demo = new AgentDemo
        {
            Id = Hex(6), Business = Line((string?)profile["business"], 100) is { Length: > 0 } b ? b : business, Url = url?.ToString() ?? "", Email = mail,
            Phone = Line(i.Phone, 40), Notes = notes, Lang = lang, Profile = Clip((string?)profile["system_prompt"], 12000),
            Welcome = Clip((string?)profile["welcome"], 900), Ip = current.Ip, CreatedAt = Now,
            SuggestionsJson = JsonSerializer.Serialize((profile["suggestions"] as JsonArray ?? []).Select(x => Line((string?)x, 120)).Where(x => x != "").Take(3).ToList()),
        };
        if (demo.Profile.Length < 50) throw new DomainException("تعذّر تجهيز الوكيل، حاول مرة أخرى", "server", 503);
        db.AgentDemos.Add(demo);
        var lead = new Lead
        {
            Kind = LeadKinds.Agent, Name = demo.Business, Email = mail, Phone = demo.Phone, Company = demo.Business, Service = "whatsapp-agent",
            Details = $"{DemoLink(demo.Id, lang)}\n{demo.Url}\n{notes}", Lang = lang, Ip = current.Ip, CreatedAt = Now,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.AgentDemoReady(mail, demo.Business, DemoLink(demo.Id, lang), lang));
        email.Enqueue(Templates.TeamLead(lead, site.Value.TeamEmail));
        return new TryAgentResult(true, demo.Id, demo.Business);
    }

    private string DemoLink(string id, string lang) => $"{site.Value.SiteUrl.TrimEnd('/')}{(Langs.IsAr(lang) ? "" : "/en")}/whatsapp-agent/demo/?d={id}";

    private async Task<JsonObject> BuildProfileAsync(string business, Uri? url, string notes, string siteText, string lang, CancellationToken ct)
    {
        const string system = """
            You set up WhatsApp customer-service agents for businesses. From the business information below, write the agent.
            Reply with ONE JSON object only (no prose, no code fence):
            {"business": "the business name as customers know it",
             "system_prompt": "complete instructions for the agent: who it is, the business facts it may use (services, prices, hours, locations, delivery, booking, payment, policies, contacts) copied exactly from the information, the language and dialect to use (the business's market: Saudi Arabic for Saudi businesses, Egyptian Arabic for Egypt, English for English sites; always reply in the customer's language), the tone, and these rules: answer only from the facts; never invent prices or promises; when unsure say you will check with the team; keep replies short like WhatsApp messages; use *bold* for prices; end with a next step (order, book, or hand over to staff).",
             "welcome": "the first WhatsApp message to a new customer, 1-2 short lines in the business's language",
             "suggestions": ["three short questions a customer would really ask, in the business's language"]}
            """;
        var user = $"Business name: {business}\nWebsite: {url}\nOwner's notes: {notes}\nPage language of the visitor: {lang}\n\nWebsite text:\n{siteText}";
        var res = await ai.CreateMessageAsync(new AiRequest(system, [new JsonObject { ["role"] = "user", ["content"] = user }], null, 2500, aiOpt.Value.Model), ct);
        return ParseJsonObject(res.Text) ?? throw new InvalidOperationException("no JSON in the agent profile");
    }

    /// <summary>the first JSON object in a model reply (tolerates prose or a code fence around it)</summary>
    public static JsonObject? ParseJsonObject(string text)
    {
        var a = text.IndexOf('{');
        var b = text.LastIndexOf('}');
        if (a < 0 || b <= a) return null;
        try { return JsonNode.Parse(text[a..(b + 1)]) as JsonObject; } catch (JsonException) { return null; }
    }

    /// <summary>readable text of an HTML page: title, description, then the body without scripts, styles and tags</summary>
    public static string PageText(string html, int max)
    {
        var title = TitleRe().Match(html) is { Success: true } t ? t.Groups[1].Value : "";
        var desc = DescRe().Match(html) is { Success: true } d ? d.Groups[1].Value : "";
        var body = DropRe().Replace(html, " ");
        body = TagRe().Replace(body, " ");
        var text = WebUtility.HtmlDecode($"{title}\n{desc}\n{body}");
        text = SpaceRe().Replace(text, " ").Trim();
        return text.Length <= max ? text : text[..max];
    }

    public async Task<DemoDto> Handle(GetDemoQuery q, CancellationToken ct)
    {
        var id = SessionKey(q.Id);
        var d = await db.AgentDemos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("التجربة غير موجودة", "notfound");
        return new DemoDto(true, d.Business, d.Example, d.Welcome, Suggestions(d));
    }

    private static List<string> Suggestions(AgentDemo d)
    {
        try { return JsonSerializer.Deserialize<List<string>>(d.SuggestionsJson) ?? []; } catch (JsonException) { return []; }
    }

    public async Task<DemoReply> Handle(DemoChatCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var id = SessionKey(i.DemoId);
        var text = Clip(i.Message, 1000);
        if (text == "") throw new DomainException("اكتب رسالتك", "input");
        var d = await db.AgentDemos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("التجربة غير موجودة", "notfound");
        var o = opt.Value;
        int left;
        if (d.Example)
        {
            var used = await quota.TryConsumeAsync($"agent:ex:{current.Ip}", o.ExampleMessagesPerIp, ct) ?? throw DomainException.Limit("انتهت رسائل التجربة لليوم");
            left = o.ExampleMessagesPerIp - used;
        }
        else
        {
            var n = await db.AgentDemos.Where(x => x.Id == id && x.Messages < o.MessagesPerDemo).ExecuteUpdateAsync(s => s.SetProperty(x => x.Messages, x => x.Messages + 1), ct);
            if (n == 0) throw DomainException.Limit("انتهت رسائل التجربة");
            left = o.MessagesPerDemo - d.Messages - 1;
        }
        if (!ai.Enabled) throw new DomainException("الخدمة غير متاحة الآن", "server", 503);

        // the visitor's last turns (from the page), alternating and starting with the customer
        var messages = new JsonArray();
        foreach (var t in (i.History ?? []).TakeLast(10))
        {
            var role = t.Role == "assistant" ? "assistant" : "user";
            var content = Clip(t.Content, 1500);
            if (content == "") continue;
            if (messages.Count == 0 && role == "assistant") continue;
            if (messages.Count > 0 && (string?)messages[^1]!["role"] == role) messages[^1]!["content"] = (string?)messages[^1]!["content"] + "\n" + content;
            else messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }
        if (messages.Count > 0 && (string?)messages[^1]!["role"] == "user") messages[^1]!["content"] = (string?)messages[^1]!["content"] + "\n" + text;
        else messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });

        var system = d.Profile + $"\n\nToday is {Riyadh.Now(clock):dddd d MMMM yyyy}. The welcome message the customer already saw: {d.Welcome}";
        try
        {
            var res = await ai.CreateMessageAsync(new AiRequest(system, messages, null, 700, aiOpt.Value.Model), ct);
            var reply = Clip(res.Text, 3000);
            if (reply == "") throw new InvalidOperationException("empty reply");
            return new DemoReply(true, reply, Math.Max(0, left));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Demo chat failed for {Demo}", id);
            if (!d.Example) await db.AgentDemos.Where(x => x.Id == id && x.Messages > 0).ExecuteUpdateAsync(s => s.SetProperty(x => x.Messages, x => x.Messages - 1), ct);
            throw new DomainException("تعذّر الرد الآن، حاول مرة أخرى", "server", 503);
        }
    }

    public async Task<Features.Public.SubmitResult> Handle(AgentSubscribeCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new Features.Public.SubmitResult(true, 0);
        var s = new AgentSubscription
        {
            DemoId = SessionKey(i.DemoId), Plan = i.Plan == "basic" ? "basic" : "pro", Name = Line(i.Name, 80), Business = Line(i.Business, 100),
            Email = Text.Email(i.Email), Phone = Line(i.Phone, 40), Whatsapp = Line(i.Whatsapp, 40), City = Line(i.City, 60), Notes = Clip(i.Notes, 1000),
            Lang = Langs.Of(i.Lang), CreatedAt = Now,
        };
        if (s.Name == "" || s.Business == "" || !IsPhone(s.Phone) || !IsPhone(s.Whatsapp) || !IsEmail(s.Email)) throw new DomainException("أكمل البيانات المطلوبة", "input");
        db.AgentSubscriptions.Add(s);
        await db.SaveChangesAsync(ct);
        var demo = s.DemoId == "" ? "" : DemoLink(s.DemoId, s.Lang);
        email.Enqueue(Templates.AgentSubscription(s, demo, site.Value.TeamEmail));
        email.Enqueue(Templates.AgentSubscriptionThanks(s, site.Value.WhatsApp));
        return new Features.Public.SubmitResult(true, s.Id);
    }

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex TitleRe();
    [GeneratedRegex(@"<meta[^>]+name=[""']description[""'][^>]+content=[""']([^""']*)", RegexOptions.IgnoreCase)] private static partial Regex DescRe();
    [GeneratedRegex(@"<(script|style|noscript|svg|template)\b[\s\S]*?</\1>|<!--[\s\S]*?-->|<head\b[\s\S]*?</head>", RegexOptions.IgnoreCase)] private static partial Regex DropRe();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRe();
    [GeneratedRegex(@"\s+")] private static partial Regex SpaceRe();
}
