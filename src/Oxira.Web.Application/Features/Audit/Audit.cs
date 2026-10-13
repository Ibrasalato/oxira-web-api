using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Audit;

public record AuditInput(string? Url, string? Name, string? Email, string? Phone, string? Lang,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record AuditCheck(string Id, bool Ok, string Value);
public record AuditScores(int Performance, int Seo, int Accessibility, int BestPractices);
public record AuditResult(bool Success, string Host, int Score, AuditScores Scores, List<AuditCheck> Checks);

public record RunAuditCommand(AuditInput Input) : ICommand<AuditResult>;

public static partial class UrlInput
{
    /// <summary>what a visitor types → an absolute http(s) URL (https:// added), or null</summary>
    public static Uri? Parse(string? raw)
    {
        var s = Line(raw, 300);
        if (s == "") return null;
        if (!Regex.IsMatch(s, "^https?://", RegexOptions.IgnoreCase)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort || u.UserInfo != "") return null;
        if (!HostRe().IsMatch(u.Host)) return null;
        return u;
    }

    [GeneratedRegex(@"^(?=.{4,200}$)([a-z0-9\u0600-\u06ff-]+\.)+[a-z\u0600-\u06ff]{2,}$", RegexOptions.IgnoreCase)] private static partial Regex HostRe();
}

/// <summary>
/// The free website check: fetches the page (SSRF-safe), runs 14 checks and turns them into four 0–100 scores.
/// Saves the run and a lead; emails the visitor and the team.
/// </summary>
public partial class AuditHandler(IAppDbContext db, TimeProvider clock, ISafeFetcher fetcher, IQuota quota, ICurrentUser current, IEmailSender email,
    IOptions<SiteOptions> site, IOptions<AuditOptions> opt) : IRequestHandler<RunAuditCommand, AuditResult>
{
    public async Task<AuditResult> Handle(RunAuditCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        var url = UrlInput.Parse(i.Url) ?? throw new DomainException("رابط الموقع غير صحيح", "url");
        var mail = Text.Email(i.Email);
        if (!IsEmail(mail)) throw new DomainException("البريد الإلكتروني غير صحيح", "email");
        var lang = Langs.Of(i.Lang);
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new AuditResult(true, url.Host, 0, new AuditScores(0, 0, 0, 0), []);
        if (await quota.TryConsumeAsync($"audit:ip:{current.Ip}", opt.Value.PerIp, ct) == null || await quota.TryConsumeAsync("audit:all", opt.Value.PerDay, ct) == null)
            throw DomainException.Limit("وصلت للحد اليومي للفحص، حاول غدًا");

        var page = await fetcher.GetAsync(url, 3_000_000, ct);
        if (page.Status == 0) throw new DomainException("تعذّر الوصول إلى الموقع", "fetch", 422);
        var final = Uri.TryCreate(page.FinalUrl, UriKind.Absolute, out var f) ? f : url;
        var root = new Uri(final.GetLeftPart(UriPartial.Authority) + "/");
        var robots = await fetcher.GetAsync(new Uri(root, "robots.txt"), 200_000, ct);
        var sitemapUrl = SitemapRe().Match(robots.Ok ? robots.Body : "") is { Success: true } sm && Uri.TryCreate(sm.Groups[1].Value.Trim(), UriKind.Absolute, out var su) ? su : new Uri(root, "sitemap.xml");
        var sitemap = await fetcher.GetAsync(sitemapUrl, 500_000, ct);

        var (checks, scores) = Evaluate(page, final, robots.Ok && robots.Body.Length > 0, sitemap.Ok && sitemap.Body.Contains("<loc", StringComparison.OrdinalIgnoreCase));
        var score = (int)Math.Round((scores.Performance + scores.Seo + scores.Accessibility + scores.BestPractices) / 4.0);
        var result = new AuditResult(true, final.Host, score, scores, checks);

        var now = clock.GetUtcNow();
        db.AuditRuns.Add(new AuditRun
        {
            Url = url.ToString(), Host = final.Host, Name = Line(i.Name, 100), Email = mail, Phone = Line(i.Phone, 40), Score = score,
            ResultJson = JsonSerializer.Serialize(result), Lang = lang, Ip = current.Ip, CreatedAt = now,
        });
        var lead = new Lead
        {
            Kind = LeadKinds.Audit, Name = Line(i.Name, 120), Email = mail, Phone = Line(i.Phone, 40), Service = "website-audit",
            Details = $"{final} · {score}/100 · " + string.Join(", ", checks.Where(c => !c.Ok).Select(c => c.Id)), Lang = lang, Ip = current.Ip, CreatedAt = now,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.AuditReport(mail, Line(i.Name, 100), result, lang, site.Value));
        email.Enqueue(Templates.TeamLead(lead, site.Value.TeamEmail));
        return result;
    }

    public static (List<AuditCheck>, AuditScores) Evaluate(FetchResult page, Uri final, bool robotsOk, bool sitemapOk)
    {
        var html = page.Body;
        string? Meta(string name) => MetaRe().Matches(html).Select(m => m.Value).Where(tag => Regex.IsMatch(tag, $"(name|property)\\s*=\\s*[\"']{Regex.Escape(name)}[\"']", RegexOptions.IgnoreCase))
            .Select(tag => ContentRe().Match(tag) is { Success: true } c ? System.Net.WebUtility.HtmlDecode(c.Groups[2].Value).Trim() : null).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        var title = TitleRe().Match(html) is { Success: true } t ? System.Net.WebUtility.HtmlDecode(Regex.Replace(t.Groups[1].Value, @"\s+", " ")).Trim() : "";
        var description = Meta("description") ?? "";
        var h1 = H1Re().Matches(html).Count;
        var imgs = ImgRe().Matches(html).Select(m => m.Value).ToList();
        var withAlt = imgs.Count(x => AltRe().IsMatch(x));
        var lang = HtmlLangRe().Match(html) is { Success: true } l ? l.Groups[1].Value : "";
        var kb = (int)Math.Round(page.Bytes / 1024.0);

        var checks = new List<AuditCheck>
        {
            new("reachable", page.Ok, page.Status.ToString()),
            new("https", final.Scheme == Uri.UriSchemeHttps, final.Scheme),
            new("title", title.Length is >= 10 and <= 70, Clip(title, 120)),
            new("description", description.Length is >= 50 and <= 170, Clip(description, 200)),
            new("h1", h1 >= 1, h1.ToString()),
            new("viewport", Meta("viewport") != null, Clip(Meta("viewport"), 80)),
            new("lang", lang != "", lang),
            new("canonical", CanonicalRe().IsMatch(html), ""),
            new("og", Meta("og:title") != null || Meta("og:image") != null, ""),
            new("schema", html.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase), ""),
            new("alt", imgs.Count == 0 || withAlt == imgs.Count, $"{withAlt}/{imgs.Count}"),
            new("robots", robotsOk, ""),
            new("sitemap", sitemapOk, ""),
            new("size", page.Bytes > 0 && page.Bytes <= 500 * 1024, $"{kb} KB"),
        };
        bool Ok(string id) => checks.First(c => c.Id == id).Ok;
        int Pct(params string[] ids) => (int)Math.Round(100.0 * ids.Count(Ok) / ids.Length);

        var perf = 100;
        if (!page.Ok) perf = 0;
        else
        {
            perf -= page.Ms > 4000 ? 45 : page.Ms > 2500 ? 30 : page.Ms > 1200 ? 15 : page.Ms > 600 ? 5 : 0;
            perf -= page.Bytes > 2_000_000 ? 40 : page.Bytes > 1_000_000 ? 25 : page.Bytes > 500_000 ? 12 : page.Bytes > 200_000 ? 4 : 0;
            perf -= Math.Min(15, Math.Max(0, imgs.Count - 30) / 2);
        }
        var scores = new AuditScores(Math.Clamp(perf, 0, 100), Pct("title", "description", "h1", "canonical", "og", "schema", "robots", "sitemap"),
            Pct("lang", "alt", "viewport", "h1"), Pct("reachable", "https", "viewport", "size"));
        return (checks, scores);
    }

    [GeneratedRegex(@"(?im)^\s*sitemap:\s*(\S+)")] private static partial Regex SitemapRe();
    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex MetaRe();
    [GeneratedRegex(@"content\s*=\s*([""'])(.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex ContentRe();
    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex TitleRe();
    [GeneratedRegex(@"<h1[\s>]", RegexOptions.IgnoreCase)] private static partial Regex H1Re();
    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex ImgRe();
    [GeneratedRegex(@"\balt\s*=", RegexOptions.IgnoreCase)] private static partial Regex AltRe();
    [GeneratedRegex(@"<html\b[^>]*\blang\s*=\s*[""']?([a-zA-Z-]{2,10})", RegexOptions.IgnoreCase)] private static partial Regex HtmlLangRe();
    [GeneratedRegex(@"<link\b[^>]*rel\s*=\s*[""']?canonical", RegexOptions.IgnoreCase)] private static partial Regex CanonicalRe();
}
