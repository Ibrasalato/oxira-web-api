using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Agent;
using Oxira.Web.Application.Features.Auth;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Studio;

public record BuilderInput(string? SessionId, string? Action, string? Message, string? Spec, string? Lang);
public record BuilderResult(bool Success, JsonObject? Spec, string Reply, bool Unavailable = false);
public record UploadResult(bool Success, string Id);
public record ImageFile(string Mime, byte[] Content);
public record ShareInput(string? Spec, string? DraftId);
public record ShareResult(bool Success, string Url, string DraftId);
public record DraftDto(bool Success, string DraftId, JsonObject Spec, DateTimeOffset UpdatedAt);

public record BuildSiteCommand(BuilderInput Input) : ICommand<BuilderResult>;
public record UploadImageCommand(byte[] Content, string? SessionId) : ICommand<UploadResult>;
public record GetImageQuery(string Id) : IQuery<ImageFile>;
public record ShareDraftCommand(ShareInput Input) : ICommand<ShareResult>;
public record GetDraftQuery(string Id) : IQuery<DraftDto>;

public static class SpecJson
{
    public const int MaxBytes = 400_000;
    /// <summary>stored and published as readable UTF-8 (Arabic not escaped); pages escape when they render</summary>
    public static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>the spec as the website sends it (JSON text) → a JSON object, or a bad_request error</summary>
    public static JsonObject Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxBytes) throw new DomainException("بيانات الموقع غير صالحة", "bad_request");
        try { return JsonNode.Parse(raw) as JsonObject ?? throw new DomainException("بيانات الموقع غير صالحة", "bad_request"); }
        catch (JsonException) { throw new DomainException("بيانات الموقع غير صالحة", "bad_request"); }
    }

    public static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}

public static class BuilderPrompt
{
    public const string System = """
        You are the AI designer in Oxira Studio. A small business owner is building their website; you change its "spec" (JSON) as they ask.
        The website is rendered from the spec by a fixed template, so you only change words, colours, fonts, sections and settings.

        Spec fields (TypeScript):
          lang: 'ar'|'en'|'de'|'fr'|'ru'; name; tagline;
          colors: { primary: '#RRGGBB'; accent: '#RRGGBB'; background?: '#RRGGBB' | '' };
          font: 'modern'|'elegant'|'friendly';
          hero: { title; subtitle; cta }; about: { title; text };
          services: { title; items: { title; text }[] };            // 3-6 items
          stats: { value; label }[];                                 // 0-4
          ctaBand: { title; text; button };
          contact: { title; phone; whatsapp; email; address; hours };
          sections: ('about'|'services'|'menu'|'gallery'|'testimonials'|'faq'|'stats'|'cta'|'contact')[];   // order on the page
          testimonials: { title; items: { quote; name; role }[] };   // ONLY real quotes the owner gives you, never invent
          faq: { title; items: { q; a }[] };
          menu: { title; note; categories: { name; items: { name; desc; price }[] }[] };   // prices are text
          extras: { whatsappButton: boolean; contactForm: boolean; mapUrl: string };
        Never change "template" or "media" (images are uploaded by the owner).

        Rules: write in the site's language (spec.lang) in a natural, professional tone for the Saudi market unless told otherwise;
        never invent facts such as prices, phone numbers, addresses, awards or client counts - keep the owner's values or leave samples as they are;
        keep texts short enough for a website (hero title under 8 words).
        For action "create": rewrite all the copy for the owner's business from what they told you (keep the template's look).

        Reply with ONE JSON object only (no prose, no code fence): {"reply": "one or two friendly sentences to the owner in their language about what you changed",
        "spec": { only the fields you changed, with complete values for each changed top-level field }}.
        If the request is not about the website, return {"reply": "...", "spec": {}}.
        """;
}

/// <summary>Oxira Studio: the AI designer, image uploads (also copied to the sites' KV) and shareable previews.</summary>
public partial class StudioHandlers(IAppDbContext db, TimeProvider clock, IAnthropic ai, IQuota quota, ICurrentUser current, ISitePublisher publisher,
    IOptions<AiOptions> aiOpt, IOptions<SitesOptions> sites, ILogger<StudioHandlers> log)
    : IRequestHandler<BuildSiteCommand, BuilderResult>, IRequestHandler<UploadImageCommand, UploadResult>, IRequestHandler<GetImageQuery, ImageFile>,
      IRequestHandler<ShareDraftCommand, ShareResult>, IRequestHandler<GetDraftQuery, DraftDto>
{
    private static readonly string[] Locked = ["template", "media", "_order"];
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<BuilderResult> Handle(BuildSiteCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var spec = SpecJson.Parse(i.Spec);
        var text = Clip(i.Message, 2000);
        var lang = Langs.Of(i.Lang);
        var ar = Langs.IsAr(lang);
        var sid = SessionKey(i.SessionId);
        if (text == "" || sid.Length < 4) throw new DomainException("اكتب طلبك", "bad_request");
        if (!ai.Enabled) return new BuilderResult(false, null, "", Unavailable: true);
        if (await quota.TryConsumeAsync($"builder:s:{sid}", 60, ct) == null || await quota.TryConsumeAsync($"builder:ip:{current.Ip}", 120, ct) == null ||
            await quota.TryConsumeAsync("builder:all", 3000, ct) == null)
            return new BuilderResult(false, null, ar ? "وصلت للحد اليومي لطلبات المصمم الذكي. تقدر تكمل التعديل من الإعدادات السريعة." : "You reached today's AI designer limit. You can keep editing with the quick settings.");

        var action = i.Action == "create" ? "create" : "edit";
        var existing = spec.DeepClone().AsObject();
        foreach (var k in Locked) existing.Remove(k);
        var user = $"Action: {action}\nThe owner says: {text}\n\nCurrent spec:\n{existing.ToJsonString(SpecJson.Relaxed)}";
        try
        {
            var res = await ai.CreateMessageAsync(new AiRequest(BuilderPrompt.System, [new JsonObject { ["role"] = "user", ["content"] = user }], null, 4000, aiOpt.Value.Model), ct);
            var json = AgentHandlers.ParseJsonObject(res.Text);
            if (json == null) return new BuilderResult(false, null, Clip(res.Text, 600));
            var changes = json["spec"] as JsonObject ?? [];
            foreach (var k in Locked) changes.Remove(k);
            var reply = Clip((string?)json["reply"], 600);
            return new BuilderResult(true, (JsonObject)changes.DeepClone(), reply == "" ? "✓" : reply);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Studio builder failed for {Session}", sid);
            return new BuilderResult(false, null, "", Unavailable: true);
        }
    }

    /// <summary>JPEG, PNG or WebP (checked by content, not the file name), up to 8 MB.</summary>
    public async Task<UploadResult> Handle(UploadImageCommand cmd, CancellationToken ct)
    {
        var bytes = cmd.Content;
        if (bytes.Length == 0) throw new DomainException("اختر صورة", "file");
        if (bytes.Length > 8 * 1024 * 1024) throw new DomainException("الصورة كبيرة جدًا", "too_big", 413);
        var mime = Sniff(bytes) ?? throw new DomainException("الصورة يجب أن تكون JPG أو PNG أو WebP", "file");
        if (await quota.TryConsumeAsync($"upload:ip:{current.Ip}", 300, ct) == null) throw DomainException.Limit();
        var id = $"i_{Hex(16)}_{(mime == "image/png" ? "p" : "j")}";
        db.Uploads.Add(new Upload { Id = id, Mime = mime, Content = bytes, Size = bytes.Length, SessionId = Line(cmd.SessionId, 64), UserId = current.Id, Ip = current.Ip, CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        try { await publisher.PutBytesAsync($"img:{id}", bytes, mime, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Image {Id} not copied to the sites store", id); }
        return new UploadResult(true, id);
    }

    public static string? Sniff(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }

    public async Task<ImageFile> Handle(GetImageQuery q, CancellationToken ct)
    {
        if (!Images.IsId(q.Id)) throw DomainException.NotFound();
        var f = await db.Uploads.AsNoTracking().Where(u => u.Id == q.Id).Select(u => new ImageFile(u.Mime, u.Content)).FirstOrDefaultAsync(ct);
        return f ?? throw DomainException.NotFound();
    }

    public async Task<ShareResult> Handle(ShareDraftCommand cmd, CancellationToken ct)
    {
        var spec = SpecJson.Parse(cmd.Input.Spec);
        spec.Remove("_order");
        var raw = spec.ToJsonString(SpecJson.Relaxed);
        var id = cmd.Input.DraftId is { } given && DraftRe().IsMatch(given) ? given : null;
        var draft = id == null ? null : await db.Drafts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (draft == null)
        {
            if (await quota.TryConsumeAsync($"share:ip:{current.Ip}", 100, ct) == null) throw DomainException.Limit();
            draft = new Draft { Id = $"d_{Hex(16)}", CreatedAt = Now };
            db.Drafts.Add(draft);
        }
        draft.SpecJson = raw;
        draft.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        await publisher.PutAsync($"draft:{draft.Id}", raw, ct);
        return new ShareResult(true, $"{sites.Value.WorkerUrl.TrimEnd('/')}/p/{draft.Id}/", draft.Id);
    }

    public async Task<DraftDto> Handle(GetDraftQuery q, CancellationToken ct)
    {
        if (!DraftRe().IsMatch(q.Id ?? "")) throw DomainException.NotFound();
        var d = await db.Drafts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.Id, ct) ?? throw DomainException.NotFound();
        return new DraftDto(true, d.Id, SpecJson.Parse(d.SpecJson), d.UpdatedAt);
    }

    [GeneratedRegex("^d_[a-f0-9]{20,40}$")] public static partial Regex DraftRe();
}
