using System.Buffers;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Api.Infrastructure;

/// <summary>
/// "Jwt" section. This API issues the unified Oxira account tokens; every Oxira API validates the same issuer, audience and key.
/// Jwt__Key is set as an environment variable on the server (at least 32 characters, the same on all Oxira APIs).
/// </summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "https://api.oxira.sa";
    public string Audience { get; set; } = "oxira";
    public string Key { get; set; } = "";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 30;
    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key.Length >= 32 ? Key : throw new InvalidOperationException("Jwt:Key must be at least 32 characters")));
}

public static class OxiraClaims
{
    public const string Role = "role";
    public const string Email = "email";
    public const string Name = "name";
    public const string Stamp = "stamp";
    /// <summary>the sign-in (refresh token family)</summary>
    public const string Session = "sid";
    /// <summary>the team owner's user id while working on their account</summary>
    public const string Acting = "act";
}

/// <summary>Access tokens: HS256, sub, email, name, role, stamp, sid, jti, iat (+ act after an account switch). Uses the real clock.</summary>
public class JwtTokenIssuer(IOptions<JwtOptions> opt) : ITokenIssuer
{
    public TimeSpan RefreshLifetime => TimeSpan.FromDays(Math.Max(1, opt.Value.RefreshDays));

    public AccessToken Issue(User user, Guid sessionId, Guid? actingOwnerId)
    {
        var o = opt.Value;
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(Math.Max(1, o.AccessMinutes));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(OxiraClaims.Email, user.Email),
            new(OxiraClaims.Name, user.Name),
            new(OxiraClaims.Role, user.Role),
            new(OxiraClaims.Stamp, user.SecurityStamp),
            new(OxiraClaims.Session, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
        };
        if (actingOwnerId is { } act) claims.Add(new Claim(OxiraClaims.Acting, act.ToString()));
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, now, expires, new SigningCredentials(o.SigningKey(), SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler { MapInboundClaims = false }.WriteToken(token), new DateTimeOffset(expires));
    }
}

public class HttpCurrentUser(IHttpContextAccessor http, IConfiguration cfg) : ICurrentUser
{
    private ClaimsPrincipal? User => http.HttpContext?.User;
    private static Guid? G(string? s) => Guid.TryParse(s, out var g) ? g : null;
    public Guid? Id => G(User?.FindFirstValue(JwtRegisteredClaimNames.Sub));
    public string? Stamp => User?.FindFirstValue(OxiraClaims.Stamp);
    public Guid? SessionId => G(User?.FindFirstValue(OxiraClaims.Session));
    public Guid? ActingOwnerId => G(User?.FindFirstValue(OxiraClaims.Acting));
    public string? UserAgent => http.HttpContext?.Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Behind Cloudflare the visitor's address is in CF-Connecting-IP. It is trusted only when Proxy:TrustCloudflare is on AND the connection
    /// itself comes from Cloudflare (Proxy:CloudflareRanges); anyone else could simply send the header.
    /// </summary>
    public string Ip
    {
        get
        {
            var ctx = http.HttpContext;
            if (ctx == null) return "unknown";
            var remote = ctx.Connection.RemoteIpAddress;
            if (cfg.GetValue("Proxy:TrustCloudflare", false) && remote != null && CloudflareRanges.Contains(cfg, remote) &&
                ctx.Request.Headers.TryGetValue("CF-Connecting-IP", out var cf) && System.Net.IPAddress.TryParse(cf.ToString().Trim(), out var visitor))
                return visitor.ToString();
            return remote?.ToString() ?? "unknown";
        }
    }
}

/// <summary>Cloudflare's published edge ranges (https://www.cloudflare.com/ips/); Proxy:CloudflareRanges replaces the list.</summary>
public static class CloudflareRanges
{
    public static readonly string[] Default =
    [
        "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
        "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13", "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
        "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32", "2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32",
    ];

    private static string[]? _source;
    private static System.Net.IPNetwork[] _nets = [];

    public static bool Contains(IConfiguration cfg, System.Net.IPAddress ip)
    {
        var list = cfg.GetSection("Proxy:CloudflareRanges").Get<string[]>() is { Length: > 0 } c ? c : Default;
        if (!ReferenceEquals(list, _source) && (_source == null || !list.SequenceEqual(_source)))
        {
            _nets = list.Select(r => System.Net.IPNetwork.TryParse(r.Trim(), out var n) ? (System.Net.IPNetwork?)n : null).Where(n => n != null).Select(n => n!.Value).ToArray();
            _source = list;
        }
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return _nets.Any(n => n.Contains(ip));
    }
}

/// <summary>
/// Business errors become {"success":false,"reason":"…","error":"…","message":"…"} (+ numbers such as seconds or minutes) with their status;
/// anything else is logged and hidden (in Development the exception text is added as "detail").
/// </summary>
public class ErrorHandler(ILogger<ErrorHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception e, CancellationToken ct)
    {
        var (status, reason, message, extra) = e switch
        {
            DomainException d => (d.Status, d.Reason, d.Message, d.Extra),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, "exists", "البيانات مسجلة مسبقًا", null),
            BadHttpRequestException b => (b.StatusCode, "bad_request", "طلب غير صالح", null),
            JsonException => (400, "bad_request", "طلب غير صالح", null),
            OperationCanceledException => (499, "cancelled", "أُلغي الطلب", (object?)null),
            _ => (500, "server", "حدث خطأ غير متوقع، حاول مرة أخرى", null),
        };
        if (status >= 500) log.LogError(e, "Unhandled error on {Path}", ctx.Request.Path);
        if (ctx.Response.HasStarted) return true;
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(ErrorBody.Of(reason, message, extra, status >= 500 && env.IsDevelopment() ? e.ToString() : null), ct);
        return true;
    }
}

public static class ErrorBody
{
    public static Dictionary<string, object?> Of(string reason, string message, object? extra = null, string? detail = null)
    {
        var body = new Dictionary<string, object?> { ["success"] = false, ["reason"] = reason, ["error"] = reason, ["message"] = message };
        if (extra != null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        if (detail != null) body["detail"] = detail[..Math.Min(detail.Length, 3000)];
        return body;
    }
}

/// <summary>
/// The site posts JSON as text/plain on purpose (no CORS preflight), and many forms as URLSearchParams.
/// text/plain → JSON; form-urlencoded → a JSON object of strings (repeated keys → array; a "data" field holding a JSON object is merged in),
/// so every endpoint binds one JSON body.
/// </summary>
public class BodyNormalizerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var type = req.ContentType;
        if (type != null && (HttpMethods.IsPost(req.Method) || HttpMethods.IsPut(req.Method) || HttpMethods.IsPatch(req.Method)))
        {
            if (type.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)) req.ContentType = "application/json; charset=utf-8";
            else if (type.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                var form = await req.ReadFormAsync(ctx.RequestAborted);
                var obj = new JsonObject();
                foreach (var (key, values) in form)
                {
                    if (key == "data" && values.Count == 1 && values[0] is { } raw && raw.TrimStart().StartsWith('{'))
                    {
                        try
                        {
                            if (JsonNode.Parse(raw) is JsonObject data)
                                foreach (var (k, v) in data.ToList()) { data.Remove(k); obj[k] = v; }
                            continue;
                        }
                        catch (JsonException) { /* keep as text */ }
                    }
                    obj[key] = values.Count == 1 ? JsonValue.Create(values[0]) : new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
                }
                var bytes = Encoding.UTF8.GetBytes(obj.ToJsonString());
                req.Body = new MemoryStream(bytes);
                req.ContentLength = bytes.Length;
                req.ContentType = "application/json; charset=utf-8";
                ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IFormFeature>(null);
            }
        }
        await next(ctx);
    }
}

// ───── lenient JSON: form posts send every value as text ─────
public sealed class LenientBool : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False or JsonTokenType.Null => false,
        JsonTokenType.Number => r.TryGetDecimal(out var d) && d != 0,
        JsonTokenType.String => (r.GetString() ?? "").Trim().ToLowerInvariant() is "1" or "true" or "on" or "yes",
        _ => throw new JsonException("boolean expected"),
    };
    public override void Write(Utf8JsonWriter w, bool v, JsonSerializerOptions o) => w.WriteBooleanValue(v);
}

public sealed class LenientNullableBool : JsonConverter<bool?>
{
    public override bool HandleNull => true;
    public override bool? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        r.TokenType == JsonTokenType.Null || r.TokenType == JsonTokenType.String && string.IsNullOrWhiteSpace(r.GetString()) ? null : new LenientBool().Read(ref r, typeof(bool), o);
    public override void Write(Utf8JsonWriter w, bool? v, JsonSerializerOptions o) { if (v is { } b) w.WriteBooleanValue(b); else w.WriteNullValue(); }
}

public sealed class LenientInt : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
    {
        JsonTokenType.Number => r.TryGetInt32(out var i) ? i : (int)Math.Round(r.GetDouble()),
        JsonTokenType.String => int.TryParse((r.GetString() ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i
            : decimal.TryParse((r.GetString() ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0,
        JsonTokenType.Null => 0,
        JsonTokenType.True => 1,
        JsonTokenType.False => 0,
        _ => throw new JsonException("number expected"),
    };
    public override void Write(Utf8JsonWriter w, int v, JsonSerializerOptions o) => w.WriteNumberValue(v);
}

public sealed class LenientNullableInt : JsonConverter<int?>
{
    public override bool HandleNull => true;
    public override int? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        r.TokenType == JsonTokenType.Null || r.TokenType == JsonTokenType.String && string.IsNullOrWhiteSpace(r.GetString()) ? null : new LenientInt().Read(ref r, typeof(int), o);
    public override void Write(Utf8JsonWriter w, int? v, JsonSerializerOptions o) { if (v is { } i) w.WriteNumberValue(i); else w.WriteNullValue(); }
}

public sealed class LenientDecimal : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
    {
        JsonTokenType.Number => r.GetDecimal(),
        JsonTokenType.String => decimal.TryParse((r.GetString() ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0,
        JsonTokenType.Null => 0,
        _ => throw new JsonException("number expected"),
    };
    public override void Write(Utf8JsonWriter w, decimal v, JsonSerializerOptions o) => w.WriteNumberValue(v);
}

/// <summary>a text field also accepts a number or true/false (and keeps an object or array as its JSON)</summary>
public sealed class LenientString : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.String: return r.GetString();
            case JsonTokenType.Number: return Encoding.UTF8.GetString(r.HasValueSequence ? r.ValueSequence.ToArray() : r.ValueSpan.ToArray());
            case JsonTokenType.True: return "true";
            case JsonTokenType.False: return "false";
            case JsonTokenType.Null: return null;
            default:
                using (var doc = JsonDocument.ParseValue(ref r)) return doc.RootElement.GetRawText();
        }
    }
    public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) => w.WriteStringValue(v);
}

public static class Json
{
    public static void Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PropertyNameCaseInsensitive = true;
        o.Converters.Add(new LenientBool());
        o.Converters.Add(new LenientNullableBool());
        o.Converters.Add(new LenientInt());
        o.Converters.Add(new LenientNullableInt());
        o.Converters.Add(new LenientDecimal());
        o.Converters.Add(new LenientString());
    }
}

/// <summary>OpenAPI document: title and the JWT bearer scheme (Swagger UI → Authorize).</summary>
internal sealed class OpenApiInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Oxira API",
            Version = "v1",
            Description = "oxira.sa: حساب أوكسيرا الموحّد (JWT)، النماذج، المحادثة الذكية، فحص المواقع، وكيل واتساب، الاستوديو والطلبات والدفع (Moyasar)، بوابة العملاء، SEO والتواصل الاجتماعي، وحالة الخدمات.",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
            Description = "Oxira account access token (POST /api/auth/login or /api/auth/magic-link/complete).",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
