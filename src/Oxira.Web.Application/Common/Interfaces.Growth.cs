using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Common;

public partial interface IAppDbContext
{
    DbSet<SeoItem> SeoItems { get; }
    DbSet<SeoProfile> SeoProfiles { get; }
    DbSet<SocialProfile> SocialProfiles { get; }
    DbSet<SocialItem> SocialItems { get; }
    DbSet<StatusCheck> StatusChecks { get; }
}

/// <summary>Encrypts stored secrets (WordPress application passwords, Meta tokens) with ASP.NET Data Protection; keys live in the database.</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    /// <returns>null when the value cannot be decrypted (other keys, tampered)</returns>
    string? Unprotect(string protectedValue);
    /// <summary>a short-lived signed value (OAuth state)</summary>
    string ProtectFor(string plain, TimeSpan life);
    string? UnprotectTimed(string protectedValue);
}

public record WpResult(bool Ok, string? Error = null, string? Url = null);

/// <summary>WordPress REST API with an application password (sites hosted elsewhere). Errors: wp_url, wp_auth, wp_rights, wp_unreachable, wp_failed.</summary>
public interface IWordPressClient
{
    Task<WpResult> VerifyAsync(string siteUrl, string user, string appPassword, CancellationToken ct = default);
    Task<WpResult> PublishAsync(string siteUrl, string user, string appPassword, string title, string html, string excerpt, CancellationToken ct = default);
}

public record MetaPage(string Id, string Name, string Token, string? IgId, string? IgUsername);
public record MetaAdAccount(string Id, string Name, string Currency);
public record MetaAccount(string UserName, string UserToken, List<MetaPage> Pages, List<MetaAdAccount> AdAccounts);
public record MetaPublishResult(bool Ok, string? FbId, string? IgId, string? Error);

/// <summary>Meta Graph API: Facebook Login for the business pages, then page and Instagram publishing. Disabled without Meta:AppId and Meta:AppSecret.</summary>
public interface IMetaClient
{
    bool Enabled { get; }
    string AuthorizeUrl(string state, string redirectUri);
    Task<MetaAccount> ConnectAsync(string code, string redirectUri, CancellationToken ct = default);
    Task<MetaPublishResult> PublishAsync(MetaPage page, string imageUrl, string caption, IReadOnlyCollection<string> platforms, CancellationToken ct = default);
}

/// <summary>One uptime probe of a configured service URL.</summary>
public interface IUptimeProbe
{
    Task<(bool Ok, int Ms)> ProbeAsync(string url, CancellationToken ct = default);
}

/// <summary>"Status" section: the services shown on /status/.</summary>
public class StatusOptions
{
    public int Minutes { get; set; } = 15;
    public Dictionary<string, string> Services { get; set; } = new()
    {
        ["web"] = "https://oxira.sa/", ["events"] = "https://events.oxira.sa/", ["design"] = "https://design.oxira.sa/",
        ["api"] = "https://api.oxira.sa/health", ["automation"] = "https://n8n.oxira.sa/healthz",
    };
}

public static class JsonText
{
    public static JsonNode Parse(string? json, Func<JsonNode> fallback)
    {
        try { return (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json)) ?? fallback(); } catch (System.Text.Json.JsonException) { return fallback(); }
    }

    public static JsonObject Obj(string? json) => Parse(json, () => new JsonObject()) as JsonObject ?? [];
    public static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n is JsonValue v2 ? v2.ToJsonString().Trim('"') : "";
}
