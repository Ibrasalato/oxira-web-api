using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>ASP.NET Data Protection (keys persisted in the database, so every server and restart can read them).</summary>
public class DataProtectionSecrets(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _p = provider.CreateProtector("Oxira.Web.Secrets.v1");
    private readonly ITimeLimitedDataProtector _t = provider.CreateProtector("Oxira.Web.State.v1").ToTimeLimitedDataProtector();

    public string Protect(string plain) => _p.Protect(plain);
    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try { return _p.Unprotect(protectedValue); } catch (CryptographicException) { return null; }
    }
    public string ProtectFor(string plain, TimeSpan life) => _t.Protect(plain, life);
    public string? UnprotectTimed(string protectedValue)
    {
        try { return _t.Unprotect(protectedValue); } catch (CryptographicException) { return null; }
    }
}

/// <summary>WordPress REST API (application passwords). Requests go through the public-network client: the client's site must be a public address.</summary>
public class WordPressClient(IHttpClientFactory factory, ILogger<WordPressClient> log) : IWordPressClient
{
    private static AuthenticationHeaderValue Basic(string user, string pass) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass.Replace(" ", "")}")));

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string user, string pass, object? body, CancellationToken ct)
    {
        var http = factory.CreateClient(PublicNetwork.ClientName);
        var target = new Uri(url);
        for (var hop = 0; ; hop++)
        {
            if (!PublicNetwork.IsAllowedUrl(target)) throw new HttpRequestException("blocked");
            var req = new HttpRequestMessage(method, target);
            req.Headers.Authorization = Basic(user, pass);
            req.Headers.TryAddWithoutValidation("User-Agent", "Oxira/1.0 (+https://oxira.sa)");
            if (body != null) req.Content = JsonContent.Create(body);
            var res = await http.SendAsync(req, ct);
            if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } loc && hop < 3)
            {
                target = loc.IsAbsoluteUri ? loc : new Uri(target, loc);
                res.Dispose();
                continue;
            }
            return res;
        }
    }

    public async Task<WpResult> VerifyAsync(string siteUrl, string user, string appPassword, CancellationToken ct = default)
    {
        try
        {
            using var res = await SendAsync(HttpMethod.Get, $"{siteUrl.TrimEnd('/')}/wp-json/wp/v2/users/me?context=edit", user, appPassword, null, ct);
            if (res.StatusCode == HttpStatusCode.NotFound) return new WpResult(false, "wp_url");
            if (res.StatusCode is HttpStatusCode.Unauthorized) return new WpResult(false, "wp_auth");
            if (res.StatusCode is HttpStatusCode.Forbidden) return new WpResult(false, "wp_rights");
            if (!res.IsSuccessStatusCode) return new WpResult(false, "wp_failed");
            var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
            if (json?["id"] == null) return new WpResult(false, "wp_url");
            var caps = json["capabilities"] as JsonObject;
            return caps != null && (bool?)caps["publish_posts"] != true ? new WpResult(false, "wp_rights") : new WpResult(true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or UriFormatException)
        {
            log.LogInformation(e, "WordPress at {Url} unreachable", siteUrl);
            return new WpResult(false, "wp_unreachable");
        }
    }

    public async Task<WpResult> PublishAsync(string siteUrl, string user, string appPassword, string title, string html, string excerpt, CancellationToken ct = default)
    {
        try
        {
            using var res = await SendAsync(HttpMethod.Post, $"{siteUrl.TrimEnd('/')}/wp-json/wp/v2/posts", user, appPassword, new { title, content = html, excerpt, status = "publish" }, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized) return new WpResult(false, "wp_auth");
            if (res.StatusCode == HttpStatusCode.Forbidden) return new WpResult(false, "wp_rights");
            if (!res.IsSuccessStatusCode) return new WpResult(false, "wp_failed");
            var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
            return new WpResult(true, null, (string?)json?["link"]);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            log.LogWarning(e, "WordPress publish to {Url} failed", siteUrl);
            return new WpResult(false, "wp_unreachable");
        }
    }
}

/// <summary>"Meta" section: the Oxira Meta app (Facebook Login for Business). Secrets from environment variables.</summary>
public class MetaOptions
{
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string GraphVersion { get; set; } = "v21.0";
    public string Scopes { get; set; } = "pages_show_list,pages_read_engagement,pages_manage_posts,instagram_basic,instagram_content_publish,business_management,ads_read";
}

/// <summary>Meta Graph API: login, pages (with their Instagram accounts), ad accounts, and photo posts to Facebook and Instagram.</summary>
public class MetaClient(HttpClient http, IOptions<MetaOptions> opt) : IMetaClient
{
    private MetaOptions O => opt.Value;
    private string Graph => $"https://graph.facebook.com/{O.GraphVersion}";
    public bool Enabled => O.AppId != "" && O.AppSecret != "";

    public string AuthorizeUrl(string state, string redirectUri) =>
        $"https://www.facebook.com/{O.GraphVersion}/dialog/oauth?client_id={Uri.EscapeDataString(O.AppId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString(O.Scopes)}&response_type=code";

    private async Task<JsonNode> GetAsync(string url, CancellationToken ct)
    {
        using var res = await http.GetAsync(url, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Meta {(int)res.StatusCode}: {text[..Math.Min(400, text.Length)]}");
        return JsonNode.Parse(text)!;
    }

    private async Task<JsonNode> PostAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException((string?)JsonNode.Parse(text)?["error"]?["message"] ?? $"Meta {(int)res.StatusCode}");
        return JsonNode.Parse(text)!;
    }

    public async Task<MetaAccount> ConnectAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        string E(string s) => Uri.EscapeDataString(s);
        var shortToken = (string?)(await GetAsync($"{Graph}/oauth/access_token?client_id={E(O.AppId)}&redirect_uri={E(redirectUri)}&client_secret={E(O.AppSecret)}&code={E(code)}", ct))["access_token"]
            ?? throw new HttpRequestException("Meta returned no token");
        var token = (string?)(await GetAsync($"{Graph}/oauth/access_token?grant_type=fb_exchange_token&client_id={E(O.AppId)}&client_secret={E(O.AppSecret)}&fb_exchange_token={E(shortToken)}", ct))["access_token"] ?? shortToken;
        var me = await GetAsync($"{Graph}/me?fields=name&access_token={E(token)}", ct);
        var pages = await GetAsync($"{Graph}/me/accounts?fields=id,name,access_token,instagram_business_account{{id,username}}&limit=100&access_token={E(token)}", ct);
        var list = (pages["data"] as JsonArray ?? []).OfType<JsonObject>().Select(p => new MetaPage(
            (string?)p["id"] ?? "", (string?)p["name"] ?? "", (string?)p["access_token"] ?? "", (string?)p["instagram_business_account"]?["id"], (string?)p["instagram_business_account"]?["username"]))
            .Where(p => p.Id != "").ToList();
        var accounts = new List<MetaAdAccount>();
        try
        {
            var ads = await GetAsync($"{Graph}/me/adaccounts?fields=id,name,currency&limit=100&access_token={E(token)}", ct);
            accounts = (ads["data"] as JsonArray ?? []).OfType<JsonObject>().Select(a => new MetaAdAccount((string?)a["id"] ?? "", (string?)a["name"] ?? "", (string?)a["currency"] ?? "")).ToList();
        }
        catch (HttpRequestException) { /* no ads permission */ }
        return new MetaAccount((string?)me["name"] ?? "", token, list, accounts);
    }

    public async Task<MetaPublishResult> PublishAsync(MetaPage page, string imageUrl, string caption, IReadOnlyCollection<string> platforms, CancellationToken ct = default)
    {
        string? fb = null, ig = null;
        var errors = new List<string>();
        if (platforms.Contains("facebook"))
        {
            try { fb = (string?)(await PostAsync($"{Graph}/{page.Id}/photos", new() { ["url"] = imageUrl, ["caption"] = caption, ["access_token"] = page.Token }, ct))["post_id"]; }
            catch (HttpRequestException e) { errors.Add("Facebook: " + e.Message); }
        }
        if (platforms.Contains("instagram"))
        {
            if (page.IgId == null) errors.Add("Instagram: no business account linked to the page");
            else
            {
                try
                {
                    var container = (string?)(await PostAsync($"{Graph}/{page.IgId}/media", new() { ["image_url"] = imageUrl, ["caption"] = caption, ["access_token"] = page.Token }, ct))["id"];
                    ig = (string?)(await PostAsync($"{Graph}/{page.IgId}/media_publish", new() { ["creation_id"] = container ?? "", ["access_token"] = page.Token }, ct))["id"];
                }
                catch (HttpRequestException e) { errors.Add("Instagram: " + e.Message); }
            }
        }
        return new MetaPublishResult(errors.Count == 0 && (fb != null || ig != null), fb, ig, errors.Count == 0 ? null : string.Join(" · ", errors));
    }
}

/// <summary>Status page probes: a GET with a 10 second limit; up when the answer is 2xx or 3xx.</summary>
public class HttpUptimeProbe(HttpClient http) : IUptimeProbe
{
    public async Task<(bool Ok, int Ms)> ProbeAsync(string url, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return ((int)res.StatusCode < 400, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return (false, (int)sw.ElapsedMilliseconds); }
    }
}
