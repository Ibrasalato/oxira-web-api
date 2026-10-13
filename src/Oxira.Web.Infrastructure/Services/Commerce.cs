using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Public;
using Oxira.Web.Domain;
using Oxira.Web.Infrastructure.Data;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>Moyasar hosted invoices (https://docs.moyasar.com/api/invoices). Amounts in halalas.</summary>
public class MoyasarGateway(HttpClient http, IOptions<PaymentOptions> opt) : IPaymentGateway
{
    public string Name => "moyasar";

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var o = opt.Value;
        if (string.IsNullOrEmpty(o.MoyasarSecretKey)) throw new InvalidOperationException("Payments:MoyasarSecretKey is not set");
        var req = new HttpRequestMessage(method, $"{o.MoyasarUrl.TrimEnd('/')}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(o.MoyasarSecretKey + ":")));
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    public async Task<(string Id, string Url)> CreateInvoiceAsync(InvoiceRequest r, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Post, "/v1/invoices", new
        {
            amount = (long)Math.Round(r.Amount * 100, MidpointRounding.AwayFromZero),
            currency = "SAR",
            description = r.Description.Length > 255 ? r.Description[..255] : r.Description,
            callback_url = r.CallbackUrl,
            success_url = r.SuccessUrl,
            back_url = r.BackUrl,
            metadata = new { payment_id = r.PaymentId.ToString() },
        });
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Moyasar {(int)res.StatusCode}: {text[..Math.Min(text.Length, 300)]}");
        var j = JsonNode.Parse(text)!;
        return ((string?)j["id"] ?? throw new HttpRequestException("Moyasar returned no id"), (string?)j["url"] ?? throw new HttpRequestException("Moyasar returned no url"));
    }

    public async Task<GatewayInvoice?> GetInvoiceAsync(string id, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, $"/v1/invoices/{Uri.EscapeDataString(id)}");
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Moyasar {(int)res.StatusCode}: {text[..Math.Min(text.Length, 300)]}");
        var j = JsonNode.Parse(text)!;
        return new GatewayInvoice((string?)j["id"] ?? id, (string?)j["status"] ?? "", (long?)j["amount"] ?? 0, (string?)j["currency"] ?? "", (string?)j["metadata"]?["payment_id"]);
    }
}

public record SimInvoice(string Id, long AmountHalalas, string Description, string SuccessUrl, string BackUrl, bool Paid);

/// <summary>Payments:Simulate — a local "payment page" (GET /api/payments/simulate/{id}) instead of Moyasar, for development before the live account is active.</summary>
public interface IPaymentSimulator
{
    SimInvoice? Find(string id);
    void MarkPaid(string id);
}

public class SimulatedGateway(IOptions<SiteOptions> site) : IPaymentGateway, IPaymentSimulator
{
    private static readonly ConcurrentDictionary<string, SimInvoice> Invoices = new();
    public string Name => "simulated";

    public Task<(string Id, string Url)> CreateInvoiceAsync(InvoiceRequest r, CancellationToken ct)
    {
        var id = "sim_" + Text.Hex(10);
        Invoices[id] = new SimInvoice(id, (long)Math.Round(r.Amount * 100, MidpointRounding.AwayFromZero), r.Description, r.SuccessUrl, r.BackUrl, false);
        return Task.FromResult((id, $"{site.Value.ApiUrl.TrimEnd('/')}/api/payments/simulate/{id}"));
    }

    public Task<GatewayInvoice?> GetInvoiceAsync(string id, CancellationToken ct) =>
        Task.FromResult(Invoices.TryGetValue(id, out var i) ? new GatewayInvoice(i.Id, i.Paid ? "paid" : "initiated", i.AmountHalalas, "SAR", null) : null);

    public SimInvoice? Find(string id) => Invoices.GetValueOrDefault(id);
    public void MarkPaid(string id) { if (Invoices.TryGetValue(id, out var i)) Invoices[id] = i with { Paid = true }; }
}

/// <summary>"Cloudflare" section: the KV namespace bound to the sites worker as SITES. The API token needs "Workers KV Storage: Edit".</summary>
public class CloudflareOptions
{
    public string AccountId { get; set; } = "";
    public string NamespaceId { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public bool Configured => AccountId != "" && NamespaceId != "" && ApiToken != "";
}

/// <summary>Cloudflare Workers KV REST API (values are written as-is; the worker reads JSON specs and image bytes).</summary>
public class CloudflareKvPublisher(HttpClient http, IOptions<CloudflareOptions> opt, ILogger<CloudflareKvPublisher> log) : ISitePublisher
{
    public bool Enabled => opt.Value.Configured;

    private string Url(string key) =>
        $"https://api.cloudflare.com/client/v4/accounts/{opt.Value.AccountId}/storage/kv/namespaces/{opt.Value.NamespaceId}/values/{Uri.EscapeDataString(key)}";

    private async Task SendAsync(HttpMethod method, string key, HttpContent? content, CancellationToken ct)
    {
        if (!Enabled) { log.LogInformation("KV {Method} {Key} (Cloudflare not configured)", method, key); return; }
        using var req = new HttpRequestMessage(method, Url(key)) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.Value.ApiToken);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode && !(method == HttpMethod.Delete && res.StatusCode == HttpStatusCode.NotFound))
            throw new HttpRequestException($"Cloudflare KV {method} {key}: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
    }

    public Task PutAsync(string key, string value, CancellationToken ct = default) => SendAsync(HttpMethod.Put, key, new StringContent(value, Encoding.UTF8, "text/plain"), ct);

    public Task PutBytesAsync(string key, byte[] value, string contentType, CancellationToken ct = default)
    {
        var c = new ByteArrayContent(value);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return SendAsync(HttpMethod.Put, key, c, ct);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (!Enabled) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, Url(key));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.Value.ApiToken);
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, key, null, ct);
}

/// <summary>Proof strip: sites delivered before this API (Proof:BaseSites) plus paid or published studio sites.</summary>
public class OrderSiteCounter(OxiraDbContext db, IConfiguration cfg) : ISiteCounter
{
    public async Task<int> PublishedSitesAsync(CancellationToken ct) =>
        cfg.GetValue("Proof:BaseSites", 0) + await db.Orders.CountAsync(o => o.Kind != OrderKinds.External && o.PaidAt != null, ct);
}
