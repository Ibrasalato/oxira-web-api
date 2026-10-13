using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Application.Features.Agent;
using Oxira.Web.Application.Features.Auth;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Application.Features.Seo;
using Oxira.Web.Application.Features.Studio;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Social;

public record SocialOrderInput([property: JsonPropertyName("order_id")] int? OrderId, string? Lang);
public record SocialSelectInput([property: JsonPropertyName("order_id")] int? OrderId, [property: JsonPropertyName("page_id")] string? PageId,
    [property: JsonPropertyName("ad_account_id")] string? AdAccountId);
public record SocialBriefInput([property: JsonPropertyName("order_id")] int? OrderId, string? About, string? Audience, string? Offers, string? Areas, string? Avoid, string? Voice,
    string? Lang, string? Time, List<string>? Platforms, List<string>? Photos, string? Competitors, bool? Replies,
    [property: JsonPropertyName("ads_cap")] decimal? AdsCap, [property: JsonPropertyName("ads_goal")] string? AdsGoal,
    [property: JsonPropertyName("age_min")] int? AgeMin, [property: JsonPropertyName("age_max")] int? AgeMax, string? Special);
public record SocialDecisionInput([property: JsonPropertyName("order_id")] int? OrderId, [property: JsonPropertyName("item_id")] int? ItemId, string? Title, string? Caption,
    string? Hashtags, string? Day, string? Time, List<string>? Platforms, string? Image, decimal? Budget, int? Days, string? Text, string? Headline);

public record SocialItemDto(int Id, string Kind, string Month, string Status, string Title, string Caption, string Hashtags, string Day, string Time, string Platforms,
    string Bg, string Image, string Idea, JsonNode Ad, JsonNode Result);
public record SocialView(bool Success, List<SocialItemDto> Items, JsonObject Settings, string Plan, bool Ads, bool MetaReady, JsonNode? Report);
public record UrlResult(bool Success, string Url);

public record SocialListQuery(int OrderId) : IQuery<SocialView>;
public record MetaStartCommand(SocialOrderInput Input) : ICommand<UrlResult>;
public record MetaCallbackCommand(string? Code, string? State, string? Error) : ICommand<string>;
public record MetaDisconnectCommand(int OrderId) : ICommand<Done>;
public record MetaSelectCommand(SocialSelectInput Input) : ICommand<Done>;
public record SocialSaveBriefCommand(SocialBriefInput Input) : ICommand<Done>;
public record SocialApproveCommand(SocialDecisionInput Input) : ICommand<Done>;
public record SocialRejectCommand(SocialDecisionInput Input) : ICommand<Done>;
/// <summary>scheduled: the monthly plan for every site with the social add-on</summary>
public record DraftSocialCommand : ICommand<int>;
/// <summary>scheduled: approved posts whose time has come are published to Facebook / Instagram</summary>
public record PublishDueSocialCommand : ICommand<int>;
/// <summary>scheduled: the monthly content pack (content add-on)</summary>
public record DraftContentCommand : ICommand<int>;

/// <summary>
/// The social media add-on: Meta connection (Facebook Login, tokens encrypted), the brief, a monthly plan written by Claude,
/// owner approval, then scheduled publishing. Ads are approved here and launched by the team (Meta Marketing API not automated).
/// </summary>
public class SocialHandlers(IAppDbContext db, TimeProvider clock, Access access, IMetaClient meta, ISecretProtector secrets, IAnthropic ai, Notifier notifier,
    IEmailSender email, IOptions<SiteOptions> site, IOptions<AiOptions> aiOpt, ILogger<SocialHandlers> log)
    : IRequestHandler<SocialListQuery, SocialView>, IRequestHandler<MetaStartCommand, UrlResult>, IRequestHandler<MetaCallbackCommand, string>,
      IRequestHandler<MetaDisconnectCommand, Done>, IRequestHandler<MetaSelectCommand, Done>, IRequestHandler<SocialSaveBriefCommand, Done>,
      IRequestHandler<SocialApproveCommand, Done>, IRequestHandler<SocialRejectCommand, Done>, IRequestHandler<DraftSocialCommand, int>,
      IRequestHandler<PublishDueSocialCommand, int>, IRequestHandler<DraftContentCommand, int>
{
    private static readonly string[] SocialProducts = ["social", "socialpro"];
    private DateTimeOffset Now => clock.GetUtcNow();
    private string Callback => $"{site.Value.ApiUrl.TrimEnd('/')}/api/social/meta/callback";

    private async Task<SocialProfile> ProfileAsync(int orderId, CancellationToken ct)
    {
        var p = await db.SocialProfiles.FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (p != null) return p;
        p = new SocialProfile { OrderId = orderId, UpdatedAt = Now };
        db.SocialProfiles.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    private static decimal Dec(JsonNode? n) => decimal.TryParse(JsonText.Str(n), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static SocialItemDto Dto(SocialItem x) => new(x.Id, x.Kind, x.Month, x.Status, x.Title, x.Caption, x.Hashtags, x.Day, x.Time, x.Platforms, x.Bg, x.Image, x.Idea,
        JsonText.Parse(x.AdJson, () => new JsonObject()), JsonText.Parse(x.ResultJson, () => new JsonObject()));

    public async Task<SocialView> Handle(SocialListQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var o = await PortalHandlers.OrderOfAsync(db, a, q.OrderId, ct);
        var p = await ProfileAsync(o.Id, ct);
        var plan = await Growth.PlanAsync(db, o.Id, SocialProducts, ct);
        var ads = await Growth.PlanAsync(db, o.Id, ["ads"], ct) != "";
        var since = Riyadh.Now(clock).AddMonths(-3).ToString("yyyy-MM");
        var items = await db.SocialItems.AsNoTracking().Where(i => i.OrderId == o.Id && string.Compare(i.Month, since) >= 0).OrderBy(i => i.Day).ThenBy(i => i.Id).Take(400).ToListAsync(ct);
        var settings = new JsonObject { ["brief"] = JsonText.Obj(p.BriefJson), ["meta"] = JsonText.Obj(p.MetaJson) };
        return new SocialView(true, items.Select(Dto).ToList(), settings, plan == "socialpro" ? "pro" : plan == "social" ? "basic" : "", ads, meta.Enabled,
            p.ReportJson == null ? null : JsonText.Parse(p.ReportJson, () => new JsonObject()));
    }

    // ───── Meta connection ─────
    public async Task<UrlResult> Handle(MetaStartCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var o = await PortalHandlers.OrderOfAsync(db, a, cmd.Input.OrderId, ct);
        if (!meta.Enabled) throw new DomainException("الربط مع ميتا غير متاح بعد", "meta_not_ready");
        var state = secrets.ProtectFor(JsonSerializer.Serialize(new { o = o.Id, u = a.Owner.Id, l = Langs.Of(cmd.Input.Lang ?? a.Owner.Lang) }), TimeSpan.FromMinutes(20));
        return new UrlResult(true, meta.AuthorizeUrl(state, Callback));
    }

    /// <returns>where to send the browser back to (the account page, ?social=connected or ?social=error)</returns>
    public async Task<string> Handle(MetaCallbackCommand cmd, CancellationToken ct)
    {
        var raw = cmd.State == null ? null : secrets.UnprotectTimed(cmd.State);
        var state = raw == null ? null : JsonNode.Parse(raw);
        var lang = Langs.Of(JsonText.Str(state?["l"]));
        var back = site.Value.Account(lang);
        if (state == null || cmd.Error != null || string.IsNullOrEmpty(cmd.Code)) return back + "?social=error#social";
        var orderId = (int?)state["o"] ?? 0;
        var userId = Guid.TryParse(JsonText.Str(state["u"]), out var g) ? g : Guid.Empty;
        var o = await db.Orders.FirstOrDefaultAsync(x => x.Id == orderId && x.UserId == userId, ct);
        if (o == null) return back + "?social=error#social";
        try
        {
            var acc = await meta.ConnectAsync(cmd.Code, Callback, ct);
            var p = await ProfileAsync(o.Id, ct);
            var first = acc.Pages.FirstOrDefault();
            p.MetaJson = new JsonObject
            {
                ["connected"] = true, ["connected_at"] = Now.UtcDateTime.ToString("O"), ["user_name"] = acc.UserName,
                ["pages"] = new JsonArray(acc.Pages.Select(x => (JsonNode?)new JsonObject { ["id"] = x.Id, ["name"] = x.Name, ["ig_username"] = x.IgUsername }).ToArray()),
                ["adaccounts"] = new JsonArray(acc.AdAccounts.Select(x => (JsonNode?)new JsonObject { ["id"] = x.Id, ["name"] = x.Name, ["currency"] = x.Currency }).ToArray()),
                ["page_id"] = first?.Id, ["ig_username"] = first?.IgUsername, ["ad_account_id"] = acc.AdAccounts.FirstOrDefault()?.Id,
            }.ToJsonString(SpecJson.Relaxed);
            p.MetaSecret = secrets.Protect(JsonSerializer.Serialize(new { user = acc.UserToken, pages = acc.Pages }));
            p.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
            return back + "?social=connected#social";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Meta connection for order {Order} failed", orderId);
            return back + "?social=error#social";
        }
    }

    public async Task<Done> Handle(MetaDisconnectCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var o = await PortalHandlers.OrderOfAsync(db, a, cmd.OrderId, ct);
        var p = await ProfileAsync(o.Id, ct);
        p.MetaJson = "{}";
        p.MetaSecret = "";
        p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public async Task<Done> Handle(MetaSelectCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var o = await PortalHandlers.OrderOfAsync(db, a, cmd.Input.OrderId, ct);
        var p = await ProfileAsync(o.Id, ct);
        var m = JsonText.Obj(p.MetaJson);
        var pages = m["pages"] as JsonArray ?? [];
        var accounts = m["adaccounts"] as JsonArray ?? [];
        if (!string.IsNullOrEmpty(cmd.Input.PageId))
        {
            var page = pages.FirstOrDefault(x => JsonText.Str(x?["id"]) == cmd.Input.PageId) ?? throw new DomainException("الصفحة غير موجودة", "bad_request");
            m["page_id"] = cmd.Input.PageId;
            m["ig_username"] = JsonText.Str(page["ig_username"]);
        }
        if (cmd.Input.AdAccountId != null)
        {
            if (cmd.Input.AdAccountId != "" && accounts.All(x => JsonText.Str(x?["id"]) != cmd.Input.AdAccountId)) throw new DomainException("الحساب الإعلاني غير موجود", "bad_request");
            m["ad_account_id"] = cmd.Input.AdAccountId == "" ? null : cmd.Input.AdAccountId;
        }
        p.MetaJson = m.ToJsonString(SpecJson.Relaxed);
        p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public async Task<Done> Handle(SocialSaveBriefCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var i = cmd.Input;
        var o = await PortalHandlers.OrderOfAsync(db, a, i.OrderId, ct);
        var p = await ProfileAsync(o.Id, ct);
        JsonArray Arr(IEnumerable<string> xs) => new(xs.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        p.BriefJson = new JsonObject
        {
            ["about"] = Clip(i.About, 1500), ["audience"] = Clip(i.Audience, 600), ["offers"] = Clip(i.Offers, 800), ["areas"] = Line(i.Areas, 300), ["avoid"] = Line(i.Avoid, 600),
            ["voice"] = Line(i.Voice, 20), ["lang"] = Line(i.Lang, 10), ["time"] = Line(i.Time, 5) is { Length: 5 } t ? t : "19:00",
            ["platforms"] = Arr((i.Platforms ?? []).Where(x => x is "instagram" or "facebook").Distinct()),
            ["photos"] = Arr((i.Photos ?? []).Where(Images.IsId).Distinct().Take(30)),
            ["competitors"] = Arr(Growth.Lines(i.Competitors, 10, 120)), ["replies"] = i.Replies == true,
            ["ads_cap"] = Math.Max(0, i.AdsCap ?? 0), ["ads_goal"] = Line(i.AdsGoal, 20), ["age_min"] = Math.Clamp(i.AgeMin ?? 18, 18, 65), ["age_max"] = Math.Clamp(i.AgeMax ?? 65, 18, 65),
            ["special"] = Line(i.Special, 20),
        }.ToJsonString(SpecJson.Relaxed);
        p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    private async Task<(Order Order, SocialItem Item, SocialProfile Profile)> ItemAsync(SocialDecisionInput i, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var item = await db.SocialItems.FirstOrDefaultAsync(x => x.Id == i.ItemId, ct) ?? throw DomainException.NotFound();
        var o = await PortalHandlers.OrderOfAsync(db, a, item.OrderId, ct);
        if (item.Status is not (SocialStatuses.Pending or SocialStatuses.Failed)) throw new DomainException("تمت معالجة هذا العنصر", "done", 409);
        return (o, item, await ProfileAsync(o.Id, ct));
    }

    public async Task<Done> Handle(SocialApproveCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var (o, item, p) = await ItemAsync(i, ct);
        var m = JsonText.Obj(p.MetaJson);
        if (item.Kind == "reel")
        {
            item.Status = SocialStatuses.Done;
        }
        else if (item.Kind == "ad")
        {
            if (await Growth.PlanAsync(db, o.Id, ["ads"], ct) == "") throw new DomainException("اشترك في إدارة الإعلانات أولًا", "not_subscribed");
            if (!meta.Enabled) throw new DomainException("الربط مع ميتا غير متاح بعد", "meta_not_ready");
            if (JsonText.Str(m["ad_account_id"]) == "") throw new DomainException("اختر الحساب الإعلاني", "no_ad_account");
            var ad = JsonText.Obj(item.AdJson);
            var brief = JsonText.Obj(p.BriefJson);
            var budget = i.Budget ?? Dec(ad["budget"]);
            var cap = Dec(brief["ads_cap"]);
            if (cap > 0 && budget > cap) throw new DomainException("الميزانية أعلى من الحد الشهري", "over_cap");
            if (JsonText.Str(ad["post_id"]) is { Length: > 0 } postId)
            {
                var pid = int.TryParse(postId, out var parsed) ? parsed : 0;
                var boosted = await db.SocialItems.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == o.Id && x.Id == pid, ct);
                if (boosted?.Status != SocialStatuses.Published) throw new DomainException("انشر المنشور أولًا", "post_not_published");
            }
            else if (JsonText.Str(ad["link"]) == "" && o.Url == "") throw new DomainException("أضف رابطًا للإعلان", "no_link");
            ad["budget"] = budget;
            if (i.Days is > 0) ad["days"] = Math.Clamp(i.Days.Value, 3, 30);
            if (i.Text != null) ad["text"] = Clip(i.Text, 1500);
            if (i.Headline != null) ad["headline"] = Line(i.Headline, 80);
            item.AdJson = ad.ToJsonString(SpecJson.Relaxed);
            item.Status = SocialStatuses.Approved;
            email.Enqueue(Templates.TeamAdApproved(o, item, site.Value.TeamEmail));
        }
        else
        {
            if (await Growth.PlanAsync(db, o.Id, SocialProducts, ct) == "") throw new DomainException("اشترك في خدمة التواصل الاجتماعي أولًا", "not_subscribed");
            if (!Images.IsId(i.Image)) throw new DomainException("صمّم صورة المنشور أولًا", "no_image");
            var day = Line(i.Day ?? item.Day, 10);
            var time = Line(i.Time ?? item.Time, 5);
            if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || !TimeOnly.TryParseExact(time, "HH:mm", out var t))
                throw new DomainException("اختر اليوم والوقت", "bad_request");
            if (new DateTimeOffset(d.ToDateTime(t), Riyadh.Offset) < Riyadh.Now(clock)) throw new DomainException("الموعد في الماضي", "past");
            if (i.Title != null) item.Title = Line(i.Title, 120);
            if (i.Caption != null) item.Caption = Clip(i.Caption, 2200);
            if (i.Hashtags != null) item.Hashtags = Line(i.Hashtags, 400);
            var platforms = (i.Platforms ?? []).Where(x => x is "instagram" or "facebook").Distinct().ToList();
            item.Platforms = platforms.Count > 0 ? string.Join(",", platforms) : item.Platforms;
            item.Day = day;
            item.Time = time;
            item.Image = i.Image!;
            item.Status = SocialStatuses.Approved;
        }
        item.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    public async Task<Done> Handle(SocialRejectCommand cmd, CancellationToken ct)
    {
        var (_, item, _) = await ItemAsync(cmd.Input, ct);
        item.Status = SocialStatuses.Rejected;
        item.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    // ───── scheduled jobs ─────
    private const string PlanSystem = """
        You are the social media manager of Oxira for a small business in Saudi Arabia. Write next month's content plan for Instagram and Facebook.
        Use only facts from the site and the owner's brief (never invent prices, offers or awards). Respect what the owner wants to avoid.
        Reply with ONE JSON object only: {"items":[{"kind":"post|reel|ad","day":"yyyy-MM-dd","title":"short headline for the design (max 8 words)",
        "caption":"the caption","hashtags":"#tag #tag","idea":"for reels: the filming script; for posts: the photo idea",
        "ad":{"goal":"traffic|messages|awareness","cities":["Riyadh"],"age_min":18,"age_max":65,"interests":["..."],"budget":300,"days":7,"text":"ad text","headline":"ad headline","why":"one line"}}]}
        Spread the days over the month; "ad" only on ad items.
        """;

    public async Task<int> Handle(DraftSocialCommand cmd, CancellationToken ct)
    {
        if (!ai.Enabled) return 0;
        var now = Riyadh.Now(clock);
        var target = now.Day >= 20 ? now.AddMonths(1) : now;
        var month = target.ToString("yyyy-MM");
        var subs = await db.Subscriptions.AsNoTracking().Where(s => SocialProducts.Contains(s.Product) && (s.Status == SubStatuses.Active || s.Status == SubStatuses.Cancelling))
            .Select(s => new { s.OrderId, s.Product }).ToListAsync(ct);
        var added = 0;
        foreach (var s in subs.GroupBy(x => x.OrderId))
        {
            try
            {
                var p = await ProfileAsync(s.Key, ct);
                if (p.LastPlanMonth == month || await db.SocialItems.AnyAsync(i => i.OrderId == s.Key && i.Month == month && i.Kind != "reply", ct)) continue;
                var o = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == s.Key, ct);
                var pro = s.Any(x => x.Product == "socialpro");
                var ads = await Growth.PlanAsync(db, s.Key, ["ads"], ct) != "";
                var brief = JsonText.Obj(p.BriefJson);
                var first = new DateOnly(target.Year, target.Month, 1);
                var want = $"{(pro ? 16 : 10)} post{(pro ? ", 4 reel" : "")}{(ads ? ", 2 ad" : "")}";
                var user = $"{Growth.SiteText(o)}\n\nOwner's brief: {brief.ToJsonString(SpecJson.Relaxed)}\nMonth: {month} (days {first:yyyy-MM-dd} to {first.AddMonths(1).AddDays(-1):yyyy-MM-dd})\nWrite: {want}.";
                var res = await ai.CreateMessageAsync(new AiRequest(PlanSystem, [new JsonObject { ["role"] = "user", ["content"] = user }], null, 8000, aiOpt.Value.Model), ct);
                var items = AgentHandlers.ParseJsonObject(res.Text)?["items"] as JsonArray ?? [];
                var photos = (brief["photos"] as JsonArray ?? []).Select(JsonText.Str).Where(Images.IsId).ToList();
                var time = JsonText.Str(brief["time"]) is { Length: 5 } bt ? bt : "19:00";
                var platforms = brief["platforms"] is JsonArray pl && pl.Count > 0 ? string.Join(",", pl.Select(JsonText.Str)) : "instagram,facebook";
                var n = 0;
                foreach (var x in items.OfType<JsonObject>())
                {
                    var kind = JsonText.Str(x["kind"]);
                    if (kind is not ("post" or "reel" or "ad") || kind == "ad" && !ads || kind == "reel" && !pro) continue;
                    var day = DateOnly.TryParseExact(JsonText.Str(x["day"]), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Month == first.Month ? d : first.AddDays(n % 28);
                    var ad = x["ad"] as JsonObject;
                    if (ad != null && JsonText.Str(ad["link"]) == "") ad["link"] = o.Url;
                    db.SocialItems.Add(new SocialItem
                    {
                        OrderId = s.Key, Month = month, Kind = kind, Title = Line(JsonText.Str(x["title"]), 120), Caption = Clip(JsonText.Str(x["caption"]), 2200),
                        Hashtags = Line(JsonText.Str(x["hashtags"]), 400), Idea = Clip(JsonText.Str(x["idea"]), 1500), Day = day.ToString("yyyy-MM-dd"), Time = time,
                        Platforms = platforms, Bg = photos.Count > 0 ? photos[n % photos.Count] : "", AdJson = kind == "ad" && ad != null ? ad.ToJsonString(SpecJson.Relaxed) : "{}",
                        CreatedAt = Now, UpdatedAt = Now,
                    });
                    n++;
                }
                p.LastPlanMonth = month;
                await db.SaveChangesAsync(ct);
                if (n > 0 && o.UserId is { } owner) await notifier.NotifyAsync(owner, NotificationTypes.Social, product: "social", site: o.SiteName, n: n, orderId: o.Id, ct: ct);
                added += n;
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Social plan for order {Order} failed", s.Key); }
        }
        return added;
    }

    private record PageTokens(string User, List<MetaPage> Pages);

    public async Task<int> Handle(PublishDueSocialCommand cmd, CancellationToken ct)
    {
        var now = Riyadh.Now(clock);
        var today = now.ToString("yyyy-MM-dd");
        var due = (await db.SocialItems.Where(i => i.Kind == "post" && i.Status == SocialStatuses.Approved && string.Compare(i.Day, today) <= 0).Take(50).ToListAsync(ct))
            .Where(i => string.Compare(i.Day, today) < 0 || string.Compare(i.Time, now.ToString("HH:mm")) <= 0).ToList();
        var published = 0;
        foreach (var item in due)
        {
            var result = new JsonObject();
            try
            {
                var p = await db.SocialProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == item.OrderId, ct);
                var m = JsonText.Obj(p?.MetaJson);
                var tokens = p == null || p.MetaSecret == "" ? null : secrets.Unprotect(p.MetaSecret) is { } raw ? JsonSerializer.Deserialize<PageTokens>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
                var page = tokens?.Pages.FirstOrDefault(x => x.Id == JsonText.Str(m["page_id"]));
                if (!meta.Enabled || page == null) { result["error"] = "Meta is not connected"; item.Status = SocialStatuses.Failed; }
                else
                {
                    var caption = string.Join("\n\n", new[] { item.Caption, item.Hashtags }.Where(x => x != ""));
                    var r = await meta.PublishAsync(page, $"{site.Value.ApiUrl.TrimEnd('/')}/api/images/{item.Image}", caption, item.Platforms.Split(',', StringSplitOptions.RemoveEmptyEntries), ct);
                    if (r.FbId != null) result["fb_id"] = r.FbId;
                    if (r.IgId != null) result["ig_id"] = r.IgId;
                    if (r.Error != null) result["error"] = r.Error;
                    item.Status = r.Ok ? SocialStatuses.Published : SocialStatuses.Failed;
                    if (r.Ok) published++;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Publishing social item {Item} failed", item.Id);
                result["error"] = "publishing failed";
                item.Status = SocialStatuses.Failed;
            }
            item.ResultJson = result.ToJsonString(SpecJson.Relaxed);
            item.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
        }
        return published;
    }

    private const string ContentSystem = """
        You write a month of ready social media posts for a small business (the owner posts them). Use only facts from the site.
        Reply with ONE JSON object only: {"posts":[{"day":1,"title":"short headline (max 8 words)","caption":"...","hashtags":"#..","idea":"photo idea"}]} with 12 posts.
        """;

    public async Task<int> Handle(DraftContentCommand cmd, CancellationToken ct)
    {
        if (!ai.Enabled) return 0;
        var month = Riyadh.Month(clock);
        var ids = await db.Subscriptions.AsNoTracking().Where(s => s.Product == "content" && (s.Status == SubStatuses.Active || s.Status == SubStatuses.Cancelling))
            .Select(s => s.OrderId).Distinct().ToListAsync(ct);
        var n = 0;
        foreach (var id in ids)
        {
            if (await db.ContentPacks.AnyAsync(c => c.OrderId == id && c.Month == month, ct)) continue;
            try
            {
                var o = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                var res = await ai.CreateMessageAsync(new AiRequest(ContentSystem, [new JsonObject { ["role"] = "user", ["content"] = $"{Growth.SiteText(o)}\nMonth: {month}" }], null, 6000, aiOpt.Value.Model), ct);
                var posts = AgentHandlers.ParseJsonObject(res.Text)?["posts"] as JsonArray;
                if (posts is not { Count: > 0 }) continue;
                db.ContentPacks.Add(new ContentPack { OrderId = id, Month = month, PostsJson = posts.ToJsonString(SpecJson.Relaxed), CreatedAt = Now });
                await db.SaveChangesAsync(ct);
                n++;
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Content pack for order {Order} failed", id); }
        }
        return n;
    }
}
