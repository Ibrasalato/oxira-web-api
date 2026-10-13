using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Chat;

public record ChatInput(string? SessionId, string? Message, string? Lang, string? Page);
public record ChatReply(bool Success, string Reply);
public record ChatTurnDto(string Role, string Content, DateTimeOffset At);

public record SendChatCommand(ChatInput Input) : ICommand<ChatReply>;
public record GetChatQuery(string SessionId) : IQuery<List<ChatTurnDto>>;

public static class ChatPrompts
{
    public static string System(DateTimeOffset nowRiyadh, string lang, string whatsapp) => $"""
        You are the assistant on oxira.sa, the website of Oxira (أوكسيرا), a Saudi IT and digital transformation company.
        Today is {nowRiyadh:dddd d MMMM yyyy, HH:mm} (Riyadh time). Reply in the visitor's language (the page language is "{lang}"); in Arabic use a warm, clear Saudi tone.
        Keep replies short (2-6 lines), use "- " lists and **bold** sparingly, never invent facts, prices or promises beyond this brief.

        What Oxira offers:
        - Websites built in Oxira Studio (/studio/): pick a design, edit it with the AI designer, order and pay online. Plans (SAR, before 15% VAT):
          Starter 1,499 (domain +99), Pro 2,499 (domain included), Business 4,999 (domain included). Hosting is renewed yearly.
        - Monthly add-ons for published sites (SAR/month): online booking 99, QR menu 49, Google reviews 79, AI chat assistant 199, monthly content 499,
          event tickets (free, 5% fee per ticket), SEO 299, SEO Growth 799, social media 699, social Pro 1,499, ads management 999.
        - WhatsApp AI agents for businesses (try a free demo at /whatsapp-agent/), automation, custom software and apps, IT infrastructure and cybersecurity,
          exhibitions and events (events.oxira.sa), 3D design from AutoCAD plans (design.oxira.sa), and Classti, a K-12 school management platform.
        - A free website check at /website-audit/.
        Contact: WhatsApp {whatsapp}, email info@oxira.sa.

        When the visitor wants a quote, a call, or help that needs the team: collect name, phone (or email) and what they need, confirm the summary,
        then call save_lead ONCE. After it returns, give the request number and say the team will contact them within one working day.
        """;

    public static readonly JsonArray Tools =
    [
        new JsonObject
        {
            ["name"] = "save_lead",
            ["description"] = "Save a request from an oxira.sa visitor so the Oxira team contacts them. Call once, only after the visitor confirmed the summary. Returns the request number; the team is emailed automatically.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = Str("Visitor name as they gave it"),
                    ["phone"] = Str("Mobile number with country code, digits only, or empty string"),
                    ["email"] = Str("Email, or empty string"),
                    ["company"] = Str("Company or business name, or empty string"),
                    ["service"] = Str("What they are interested in: website, add-on, WhatsApp agent, automation, software, infrastructure, events, design, Classti, other"),
                    ["details"] = Str("A short summary of what the visitor needs, in the visitor's language"),
                },
                ["required"] = new JsonArray("name", "details"),
            },
        },
    ];

    private static JsonObject Str(string d) => new() { ["type"] = "string", ["description"] = d };
}

/// <summary>
/// The oxira.sa assistant: Claude with one tool (save_lead). Memory is the last visible turns of the session.
/// When it saves a lead the team is emailed by the server (not by the model), so the email is always complete.
/// </summary>
public class ChatHandlers(IAppDbContext db, TimeProvider clock, IAnthropic ai, IQuota quota, IEmailSender email, ICurrentUser current, IOutboundWebhook webhook,
    IOptions<AiOptions> opt, IOptions<SiteOptions> site, ILogger<ChatHandlers> log)
    : IRequestHandler<SendChatCommand, ChatReply>, IRequestHandler<GetChatQuery, List<ChatTurnDto>>
{
    public const string Channel = "oxira";

    private string Sorry(string lang) => Langs.IsAr(lang)
        ? $"المعذرة، صار خلل بسيط عندنا. جرّب مرة ثانية بعد لحظات، أو راسلنا على واتساب {site.Value.WhatsApp}."
        : $"Sorry, something went wrong on our side. Please try again in a moment, or message us on WhatsApp {site.Value.WhatsApp}.";

    public async Task<ChatReply> Handle(SendChatCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var sid = SessionKey(i.SessionId);
        var text = Clip(i.Message, 2000);
        if (sid.Length < 6 || text == "") throw new DomainException("اكتب رسالتك", "bad_request");
        var lang = Langs.Of(i.Lang);
        var page = Line(i.Page, 300);
        if (!ai.Enabled) return new ChatReply(false, Sorry(lang));

        if (await quota.TryConsumeAsync($"chat:s:{sid}", opt.Value.MessagesPerSession, ct) == null ||
            await quota.TryConsumeAsync("chat:all", opt.Value.MessagesPerDay, ct) == null)
            return new ChatReply(false, Langs.IsAr(lang)
                ? $"وصلت للحد اليومي للمحادثة. راسلنا على واتساب {site.Value.WhatsApp} ونخدمك مباشرة."
                : $"You reached today's chat limit. Message us on WhatsApp {site.Value.WhatsApp} and we will help right away.");

        var history = await db.ChatMessages.AsNoTracking().Where(m => m.Channel == Channel && m.SessionId == sid)
            .OrderByDescending(m => m.Id).Take(opt.Value.Memory).ToListAsync(ct);
        history.Reverse();
        var messages = new JsonArray();
        foreach (var m in history.SkipWhile(m => m.Role != "user")) messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });

        db.ChatMessages.Add(new ChatMessage { SessionId = sid, Channel = Channel, Role = "user", Content = text, Lang = lang, Page = page, CreatedAt = clock.GetUtcNow() });
        string reply;
        var ok = true;
        try { reply = await RunAgentAsync(ChatPrompts.System(Riyadh.Now(clock), lang, site.Value.WhatsApp), messages, lang, page, ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Chat agent failed for {Session}", sid);
            reply = Sorry(lang);
            ok = false;
        }
        reply = Clip(reply, 4000);
        if (reply == "") { reply = Sorry(lang); ok = false; }
        if (ok) db.ChatMessages.Add(new ChatMessage { SessionId = sid, Channel = Channel, Role = "assistant", Content = reply, Lang = lang, Page = page, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return new ChatReply(ok, reply);
    }

    private async Task<string> RunAgentAsync(string system, JsonArray messages, string lang, string page, CancellationToken ct)
    {
        var saved = false;
        for (var step = 0; step < opt.Value.MaxIterations; step++)
        {
            var res = await ai.CreateMessageAsync(new AiRequest(system, messages, ChatPrompts.Tools, opt.Value.MaxTokens, opt.Value.Model), ct);
            var uses = res.Content.OfType<JsonObject>().Where(b => (string?)b["type"] == "tool_use").ToList();
            if (res.StopReason != "tool_use" || uses.Count == 0) return res.Text;
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = res.Content.DeepClone() });
            var results = new JsonArray();
            foreach (var u in uses)
            {
                string output;
                if ((string?)u["name"] == "save_lead" && !saved)
                {
                    var id = await SaveLeadAsync(u["input"] as JsonObject ?? [], lang, page, ct);
                    saved = true;
                    output = JsonSerializer.Serialize(new { success = true, id });
                }
                else output = JsonSerializer.Serialize(new { success = false, error = saved ? "already saved in this reply" : "unknown tool" });
                results.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = (string?)u["id"], ["content"] = output });
            }
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
        }
        return "";
    }

    private async Task<int> SaveLeadAsync(JsonObject input, string lang, string page, CancellationToken ct)
    {
        string F(string k, int max) => Clip(input[k]?.GetValueKind() == JsonValueKind.String ? (string?)input[k] : input[k]?.ToJsonString(), max);
        var lead = new Lead
        {
            Kind = LeadKinds.Chat, Name = F("name", 120), Phone = F("phone", 40), Email = Text.Email(F("email", 160)), Company = F("company", 160),
            Service = F("service", 160), Details = F("details", 5000), Lang = lang, Page = page, Ip = current.Ip, CreatedAt = clock.GetUtcNow(),
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.TeamLead(lead, site.Value.TeamEmail));
        webhook.Enqueue("lead", new { lead.Id, lead.Kind, lead.Name, lead.Email, lead.Phone, lead.Service, lead.Details, lead.Lang });
        return lead.Id;
    }

    public async Task<List<ChatTurnDto>> Handle(GetChatQuery q, CancellationToken ct)
    {
        var sid = SessionKey(q.SessionId);
        return await db.ChatMessages.AsNoTracking().Where(m => m.Channel == Channel && m.SessionId == sid).OrderBy(m => m.Id)
            .Select(m => new ChatTurnDto(m.Role, m.Content, m.CreatedAt)).ToListAsync(ct);
    }
}
