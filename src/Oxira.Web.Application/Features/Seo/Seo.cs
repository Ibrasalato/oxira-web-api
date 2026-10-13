using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Agent;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Application.Features.Studio;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Seo;

public record SeoOrderInput([property: JsonPropertyName("order_id")] int? OrderId);
public record SeoDecisionInput([property: JsonPropertyName("order_id")] int? OrderId, [property: JsonPropertyName("item_id")] int? ItemId, bool? Edited,
    string? Title, string? Description, string? Body);
public record SeoBriefInput([property: JsonPropertyName("order_id")] int? OrderId, string? Services, string? Areas, string? Audience, string? Usp, string? Notes,
    string? Keywords, string? Competitors, List<string>? Langs, [property: JsonPropertyName("gbp_maps")] string? GbpMaps, [property: JsonPropertyName("gbp_manager")] bool? GbpManager);
public record WpConnectInput([property: JsonPropertyName("order_id")] int? OrderId, [property: JsonPropertyName("wp_url")] string? WpUrl,
    [property: JsonPropertyName("wp_user")] string? WpUser, [property: JsonPropertyName("wp_pass")] string? WpPass);

public record SeoItemDto(int Id, string Kind, string Lang, string Title, string Description, string Body, string Status, string Url, DateTimeOffset Created);
public record SeoView(bool Success, bool External, string Plan, List<SeoItemDto> Items, JsonNode? Report, JsonNode History, JsonNode Ranks, JsonNode? Health, JsonNode LeadsNow, JsonObject Settings);
public record SeoPublishResult(bool Success, string Url);

public record SeoListQuery(int OrderId) : IQuery<SeoView>;
public record SeoPublishCommand(SeoDecisionInput Input) : ICommand<SeoPublishResult>;
public record SeoRejectCommand(SeoDecisionInput Input) : ICommand<Done>;
public record SeoSaveBriefCommand(SeoBriefInput Input) : ICommand<Done>;
public record WpConnectCommand(WpConnectInput Input) : ICommand<Done>;
public record WpDisconnectCommand(int OrderId) : ICommand<Done>;
/// <summary>scheduled: new suggestions for every site with the SEO add-on (weekly per site)</summary>
public record DraftSeoCommand : ICommand<int>;

public static class Growth
{
    public static async Task<string> PlanAsync(IAppDbContext db, int orderId, string[] products, CancellationToken ct) =>
        await db.Subscriptions.AsNoTracking().Where(s => s.OrderId == orderId && products.Contains(s.Product) && (s.Status == SubStatuses.Active || s.Status == SubStatuses.Cancelling))
            .Select(s => s.Product).FirstOrDefaultAsync(ct) ?? "";

    public static List<string> Lines(string? text, int max, int each = 120) =>
        (text ?? "").Split('\n', ',', '،').Select(x => Line(x, each)).Where(x => x != "").Distinct().Take(max).ToList();

    public static string SiteText(Order o)
    {
        var spec = JsonText.Obj(o.SpecJson);
        string S(string path) => JsonText.Str(path.Split('.').Aggregate((JsonNode?)spec, (n, k) => n?[k]));
        var services = spec["services"]?["items"] is JsonArray items ? string.Join("; ", items.Select(i => $"{JsonText.Str(i?["title"])}: {JsonText.Str(i?["text"])}")) : "";
        return Clip($"Business: {o.SiteName}\nTagline: {S("tagline")}\nHero: {S("hero.title")} - {S("hero.subtitle")}\nAbout: {S("about.text")}\nServices: {services}\n" +
                    $"Contact: {S("contact.address")} {S("contact.hours")}\nSite: {o.Url}\nLanguage: {S("lang")}", 4000);
    }
}

/// <summary>
/// The SEO add-on: suggestions written by Claude (scheduled), approved by the owner, then published — to the Oxira site (seo:o&lt;id&gt; / post:o&lt;id&gt;:&lt;slug&gt;)
/// or to the client's WordPress (application password, encrypted). Reports (Search Console, ranks, PageSpeed) are shown when present.
/// </summary>
public partial class SeoHandlers(IAppDbContext db, TimeProvider clock, Access access, ISitePublisher kv, IWordPressClient wp, ISecretProtector secrets, IAnthropic ai,
    Notifier notifier, IOptions<AiOptions> aiOpt, ILogger<SeoHandlers> log)
    : IRequestHandler<SeoListQuery, SeoView>, IRequestHandler<SeoPublishCommand, SeoPublishResult>, IRequestHandler<SeoRejectCommand, Done>,
      IRequestHandler<SeoSaveBriefCommand, Done>, IRequestHandler<WpConnectCommand, Done>, IRequestHandler<WpDisconnectCommand, Done>, IRequestHandler<DraftSeoCommand, int>
{
    private static readonly string[] Products = ["seo", "seogrowth"];
    private DateTimeOffset Now => clock.GetUtcNow();

    private async Task<SeoProfile> ProfileAsync(int orderId, CancellationToken ct)
    {
        var p = await db.SeoProfiles.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (p != null) return p;
        p = new SeoProfile { OrderId = orderId, UpdatedAt = Now };
        db.SeoProfiles.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<SeoView> Handle(SeoListQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var o = await PortalHandlers.OrderOfAsync(db, a, q.OrderId, ct);
        var p = await ProfileAsync(o.Id, ct);
        var items = await db.SeoItems.AsNoTracking().Where(i => i.OrderId == o.Id).OrderByDescending(i => i.Status == "pending").ThenByDescending(i => i.Id).Take(60)
            .Select(i => new SeoItemDto(i.Id, i.Kind, i.Lang, i.Title, i.Description, i.Body, i.Status, i.Url, i.CreatedAt)).ToListAsync(ct);
        JsonNode leads = new JsonObject();
        if (o.Kind != OrderKinds.External)
        {
            try { leads = JsonText.Parse(await kv.GetAsync($"hits:o{o.Id}:{Riyadh.Month(clock)}", ct), () => new JsonObject()); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Lead counters of {Order} unavailable", o.Id); }
        }
        var settings = new JsonObject
        {
            ["brief"] = JsonText.Obj(p.BriefJson), ["gbp"] = JsonText.Obj(p.GbpJson),
            ["wp"] = p.WpUrl == "" ? new JsonObject() : new JsonObject { ["url"] = p.WpUrl, ["user"] = p.WpUser, ["ok"] = p.WpOk },
        };
        return new SeoView(true, o.Kind == OrderKinds.External, await Growth.PlanAsync(db, o.Id, Products, ct), items,
            p.ReportJson == null ? null : JsonText.Parse(p.ReportJson, () => new JsonObject()), JsonText.Parse(p.HistoryJson, () => new JsonArray()),
            JsonText.Parse(p.RanksJson, () => new JsonObject { ["data"] = new JsonArray(), ["weeks"] = new JsonArray() }),
            p.HealthJson == null ? null : JsonText.Parse(p.HealthJson, () => new JsonObject()), leads, settings);
    }

    private async Task<(Order Order, SeoItem Item)> ItemAsync(SeoDecisionInput i, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var item = await db.SeoItems.FirstOrDefaultAsync(x => x.Id == i.ItemId, ct) ?? throw DomainException.NotFound();
        var o = await PortalHandlers.OrderOfAsync(db, a, item.OrderId, ct);
        if (item.Status != "pending") throw new DomainException("تمت معالجة هذا الاقتراح", "done", 409);
        return (o, item);
    }

    public async Task<SeoPublishResult> Handle(SeoPublishCommand cmd, CancellationToken ct)
    {
        var (o, item) = await ItemAsync(cmd.Input, ct);
        var i = cmd.Input;
        if (i.Edited == true || i.Title != null)
        {
            if (i.Title != null) item.Title = Line(i.Title, 90);
            if (i.Description != null) item.Description = Line(i.Description, 180);
            if (i.Body != null) item.Body = Clip(i.Body, 30000);
        }
        if (item.Slug == "") item.Slug = Slug(item.Title, item.Id);
        var p = await ProfileAsync(o.Id, ct);
        var url = "";
        if (o.Kind == OrderKinds.External)
        {
            if (item.Kind is "article" or "local" && p.WpOk && secrets.Unprotect(p.WpSecret) is { } pass)
            {
                var r = await wp.PublishAsync(p.WpUrl, p.WpUser, pass, item.Title, Markdown.ToHtml(item.Body), item.Description, ct);
                if (!r.Ok) throw new DomainException("تعذّر النشر في ووردبريس", r.Error ?? "wp_failed", 422);
                url = r.Url ?? "";
            }
            // otherwise the owner copies it (manual)
        }
        else
        {
            var seo = JsonText.Obj(await kv.GetAsync($"seo:o{o.Id}", ct));
            if (item.Kind == "meta") seo["meta"] = new JsonObject { ["title"] = item.Title, ["description"] = item.Description };
            else if (item.Kind is "article" or "local")
            {
                var date = Riyadh.Today(clock).ToString("yyyy-MM-dd");
                var meta = new JsonObject { ["slug"] = item.Slug, ["kind"] = item.Kind, ["title"] = item.Title, ["description"] = item.Description, ["date"] = date, ["lang"] = item.Lang };
                var posts = seo["posts"] as JsonArray ?? [];
                foreach (var old in posts.Where(x => JsonText.Str(x?["slug"]) == item.Slug).ToList()) posts.Remove(old);
                posts.Insert(0, meta);
                seo["posts"] = posts;
                var full = (JsonObject)meta.DeepClone();
                full["body"] = item.Body;
                await kv.PutAsync($"post:o{o.Id}:{item.Slug}", full.ToJsonString(SpecJson.Relaxed), ct);
                url = $"{o.Url.TrimEnd('/')}/{(item.Kind == "local" ? "services" : "blog")}/{item.Slug}";
            }
            if (item.Kind is "meta" or "article" or "local") await kv.PutAsync($"seo:o{o.Id}", seo.ToJsonString(SpecJson.Relaxed), ct);
        }
        item.Status = "published";
        item.Url = url;
        item.DecidedAt = Now;
        await db.SaveChangesAsync(ct);
        return new SeoPublishResult(true, url);
    }

    public async Task<Done> Handle(SeoRejectCommand cmd, CancellationToken ct)
    {
        var (_, item) = await ItemAsync(cmd.Input, ct);
        item.Status = "rejected";
        item.DecidedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public async Task<Done> Handle(SeoSaveBriefCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var i = cmd.Input;
        var o = await PortalHandlers.OrderOfAsync(db, a, i.OrderId, ct);
        var growth = await Growth.PlanAsync(db, o.Id, ["seogrowth"], ct) != "";
        var p = await ProfileAsync(o.Id, ct);
        p.BriefJson = new JsonObject
        {
            ["services"] = Clip(i.Services, 1500), ["areas"] = Clip(i.Areas, 500), ["audience"] = Clip(i.Audience, 500), ["usp"] = Clip(i.Usp, 800), ["notes"] = Clip(i.Notes, 1500),
            ["keywords"] = new JsonArray(Growth.Lines(i.Keywords, growth ? 30 : 10, 80).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["competitors"] = new JsonArray(Growth.Lines(i.Competitors, 10, 200).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["langs"] = new JsonArray((i.Langs ?? []).Where(l => Langs.All.Contains(l)).Distinct().Take(growth ? 5 : 1).Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()),
        }.ToJsonString(SpecJson.Relaxed);
        var maps = Line(i.GbpMaps, 400);
        if (maps != "" && !maps.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new DomainException("رابط الخرائط يبدأ بـ https://", "bad_request");
        p.GbpJson = new JsonObject { ["maps"] = maps, ["manager"] = i.GbpManager == true }.ToJsonString(SpecJson.Relaxed);
        p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public async Task<Done> Handle(WpConnectCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var i = cmd.Input;
        var o = await PortalHandlers.OrderOfAsync(db, a, i.OrderId, ct);
        var url = Line(i.WpUrl, 200).TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) throw new DomainException("رابط الموقع غير صحيح", "wp_url");
        var user = Line(i.WpUser, 80);
        var pass = (i.WpPass ?? "").Trim();
        if (user == "" || pass.Length < 8 || pass.Length > 120) throw new DomainException("أدخل اسم المستخدم وكلمة مرور التطبيق", "wp_auth");
        var r = await wp.VerifyAsync(url, user, pass, ct);
        if (!r.Ok) throw new DomainException("تعذّر الربط مع ووردبريس", r.Error ?? "wp_failed", 422);
        var p = await ProfileAsync(o.Id, ct);
        p.WpUrl = url;
        p.WpUser = user;
        p.WpSecret = secrets.Protect(pass);
        p.WpOk = true;
        p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public async Task<Done> Handle(WpDisconnectCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var o = await PortalHandlers.OrderOfAsync(db, a, cmd.OrderId, ct);
        var p = await ProfileAsync(o.Id, ct);
        p.WpUrl = p.WpUser = p.WpSecret = "";
        p.WpOk = false;
        p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public static string Slug(string title, int id)
    {
        var s = SlugRe().Replace(title.ToLowerInvariant(), "-").Trim('-');
        if (s.Length > 70) s = s[..70].Trim('-');
        return s.Length < 3 ? $"post-{id}" : s;
    }

    [GeneratedRegex(@"[^a-z0-9؀-ۿ]+")] private static partial Regex SlugRe();

    // ───── scheduled drafts ─────
    private const string DraftSystem = """
        You are the SEO writer of Oxira for small businesses in Saudi Arabia. Write suggestions the owner approves before anything is published.
        Use only facts from the site and the owner's brief; never invent prices, awards, addresses or numbers. Write in the requested language.
        Reply with ONE JSON object only: {"items":[{"kind":"meta|article|local|gbp|llms","lang":"ar|en","title":"...","description":"...","body":"..."}]}
        - meta: title (max 60 chars) and description (max 155) for the home page; body empty.
        - article: a helpful blog article (title max 70, description max 155, body 500-900 words in Markdown with ## headings and "- " lists) targeting one keyword.
        - local: a service page for one service in one area (same format, 350-600 words).
        - gbp: a Google Business Profile post (title, body max 1200 chars).
        - llms: an llms.txt summary of the business for AI assistants (body only, Markdown, max 2500 chars).
        """;

    public async Task<int> Handle(DraftSeoCommand cmd, CancellationToken ct)
    {
        if (!ai.Enabled) return 0;
        var since = Now.AddDays(-7);
        var orderIds = await db.Subscriptions.AsNoTracking().Where(s => Products.Contains(s.Product) && (s.Status == SubStatuses.Active || s.Status == SubStatuses.Cancelling))
            .Select(s => s.OrderId).Distinct().ToListAsync(ct);
        var added = 0;
        foreach (var id in orderIds)
        {
            try
            {
                var o = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                var p = await ProfileAsync(id, ct);
                if (p.LastDraftAt > since) continue;
                var growth = await Growth.PlanAsync(db, id, ["seogrowth"], ct) != "";
                var pending = await db.SeoItems.CountAsync(i => i.OrderId == id && i.Status == "pending", ct);
                if (pending >= (growth ? 10 : 5)) continue;
                var hasLlms = await db.SeoItems.AnyAsync(i => i.OrderId == id && i.Kind == "llms", ct);
                var brief = JsonText.Obj(p.BriefJson);
                var langs = (brief["langs"] as JsonArray ?? []).Select(JsonText.Str).Where(l => l != "").ToList();
                if (langs.Count == 0) langs.Add(Langs.Of(JsonText.Str(JsonText.Obj(o.SpecJson)["lang"])));
                var recent = await db.SeoItems.AsNoTracking().Where(i => i.OrderId == id).OrderByDescending(i => i.Id).Take(30).Select(i => i.Title).ToListAsync(ct);
                var want = growth ? "1 meta, 2 article, 2 local, 2 gbp" : "1 meta, 1 article, 1 local, 1 gbp";
                if (!hasLlms) want += ", 1 llms";
                var user = $"{Growth.SiteText(o)}\n\nOwner's brief: {brief.ToJsonString(SpecJson.Relaxed)}\nLanguages: {string.Join(", ", langs)}\n" +
                           $"Avoid repeating these titles: {string.Join(" | ", recent)}\n\nWrite: {want}.";
                var res = await ai.CreateMessageAsync(new AiRequest(DraftSystem, [new JsonObject { ["role"] = "user", ["content"] = user }], null, 8000, aiOpt.Value.Model), ct);
                var items = AgentHandlers.ParseJsonObject(res.Text)?["items"] as JsonArray ?? [];
                var n = 0;
                foreach (var x in items.OfType<JsonObject>())
                {
                    var kind = JsonText.Str(x["kind"]);
                    if (!SeoKinds.All.Contains(kind)) continue;
                    db.SeoItems.Add(new SeoItem
                    {
                        OrderId = id, Kind = kind, Lang = Langs.Of(JsonText.Str(x["lang"])), Title = Line(JsonText.Str(x["title"]), 90),
                        Description = Line(JsonText.Str(x["description"]), 180), Body = Clip(JsonText.Str(x["body"]), 30000), CreatedAt = Now,
                    });
                    n++;
                }
                p.LastDraftAt = Now;
                await db.SaveChangesAsync(ct);
                if (n > 0 && o.UserId is { } owner) await notifier.NotifyAsync(owner, NotificationTypes.Seo, product: "seo", site: o.SiteName, n: n, orderId: id, ct: ct);
                added += n;
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "SEO drafts for order {Order} failed", id); }
        }
        return added;
    }
}

/// <summary>The same small Markdown the sites worker renders (## / ### headings, paragraphs, "- " lists, **bold**), for WordPress.</summary>
public static partial class Markdown
{
    public static string ToHtml(string md)
    {
        var html = new System.Text.StringBuilder();
        var list = false;
        foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            string Inline(string t) => Bold().Replace(System.Net.WebUtility.HtmlEncode(t), "<strong>$1</strong>");
            if (line.StartsWith("- ")) { if (!list) { html.Append("<ul>"); list = true; } html.Append($"<li>{Inline(line[2..])}</li>"); continue; }
            if (list) { html.Append("</ul>"); list = false; }
            if (line.StartsWith("### ")) html.Append($"<h3>{Inline(line[4..])}</h3>");
            else if (line.StartsWith("## ")) html.Append($"<h2>{Inline(line[3..])}</h2>");
            else if (line.Trim() != "") html.Append($"<p>{Inline(line)}</p>");
        }
        if (list) html.Append("</ul>");
        return html.ToString();
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
}
