using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Tests;

public partial class TestApp
{
    public FakeAnthropic Ai { get; } = new();
    public FakeFetcher Web { get; } = new();

    partial void Fakes(IServiceCollection s)
    {
        s.RemoveAll<IAnthropic>();
        s.AddSingleton<IAnthropic>(Ai);
        s.RemoveAll<ISafeFetcher>();
        s.AddSingleton<ISafeFetcher>(Web);
        MoreFakes(s);
    }

    partial void MoreFakes(IServiceCollection s);
}

/// <summary>Scripted Claude: each call returns the next queued response (or a plain answer).</summary>
public class FakeAnthropic : IAnthropic
{
    public bool Enabled { get; set; } = true;
    public ConcurrentQueue<AiResponse> Script { get; } = new();
    public List<AiRequest> Calls { get; } = [];

    public Task<AiResponse> CreateMessageAsync(AiRequest request, CancellationToken ct = default)
    {
        lock (Calls) Calls.Add(request with { Messages = (JsonArray)request.Messages.DeepClone() });
        return Task.FromResult(Script.TryDequeue(out var r) ? r : Text("أهلاً بيك، كيف أقدر أساعدك؟"));
    }

    public static AiResponse Text(string s) => new([new JsonObject { ["type"] = "text", ["text"] = s }], "end_turn");
    public static AiResponse Tool(string name, JsonObject input) =>
        new([new JsonObject { ["type"] = "tool_use", ["id"] = "toolu_1", ["name"] = name, ["input"] = input }], "tool_use");
}

/// <summary>Visitors' websites: url → page; anything else is a 404.</summary>
public class FakeFetcher : ISafeFetcher
{
    public ConcurrentDictionary<string, FetchResult> Pages { get; } = new();
    public ConcurrentQueue<string> Calls { get; } = new();

    public void Page(string url, string body, int status = 200, int ms = 300) =>
        Pages[url] = new FetchResult(status is >= 200 and < 300, status, url, "text/html", body, System.Text.Encoding.UTF8.GetByteCount(body), ms);

    public Task<FetchResult> GetAsync(Uri url, int maxBytes = 2_000_000, CancellationToken ct = default)
    {
        Calls.Enqueue(url.ToString());
        return Task.FromResult(Pages.TryGetValue(url.ToString(), out var r) ? r : new FetchResult(false, 404, url.ToString(), "text/html", "", 0, 50));
    }
}

public partial class TestApp
{
    public FakeGateway Gateway { get; } = new();
    public FakePublisher Kv { get; } = new();

    partial void MoreFakes(IServiceCollection s)
    {
        s.RemoveAll<IPaymentGateway>();
        s.AddSingleton<IPaymentGateway>(Gateway);
        s.RemoveAll<ISitePublisher>();
        s.AddSingleton<ISitePublisher>(Kv);
        IntegrationFakes(s);
    }

    partial void IntegrationFakes(IServiceCollection s);
}

/// <summary>Moyasar stand-in: invoices are created unpaid; Pay() marks one paid (optionally with another amount).</summary>
public class FakeGateway : IPaymentGateway
{
    public string Name => "moyasar";
    public bool Down { get; set; }
    public ConcurrentDictionary<string, (InvoiceRequest Request, string Status, long Amount)> Invoices { get; } = new();

    public Task<(string Id, string Url)> CreateInvoiceAsync(InvoiceRequest request, CancellationToken ct)
    {
        if (Down) throw new HttpRequestException("gateway down");
        var id = "inv_" + Guid.NewGuid().ToString("N")[..10];
        Invoices[id] = (request, "initiated", (long)Math.Round(request.Amount * 100));
        Last = id;
        return Task.FromResult((id, $"https://checkout.moyasar.test/invoices/{id}"));
    }

    public Task<GatewayInvoice?> GetInvoiceAsync(string id, CancellationToken ct) =>
        Task.FromResult(Invoices.TryGetValue(id, out var i) ? new GatewayInvoice(id, i.Status, i.Amount, "SAR", i.Request.PaymentId.ToString()) : null);

    public void Pay(string id, long? amount = null) { var i = Invoices[id]; Invoices[id] = (i.Request, "paid", amount ?? i.Amount); }
    public string Last { get; private set; } = "";
    public string For(Guid paymentId) => Invoices.Single(x => x.Value.Request.PaymentId == paymentId).Key;
}

/// <summary>Cloudflare KV stand-in (enabled, in memory).</summary>
public class FakePublisher : ISitePublisher
{
    public bool Enabled => true;
    public ConcurrentDictionary<string, string> Keys { get; } = new();
    public ConcurrentDictionary<string, byte[]> Bytes { get; } = new();
    public Task PutAsync(string key, string value, CancellationToken ct = default) { Keys[key] = value; return Task.CompletedTask; }
    public Task PutBytesAsync(string key, byte[] value, string contentType, CancellationToken ct = default) { Bytes[key] = value; return Task.CompletedTask; }
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Keys.GetValueOrDefault(key));
    public Task DeleteAsync(string key, CancellationToken ct = default) { Keys.TryRemove(key, out _); Bytes.TryRemove(key, out _); return Task.CompletedTask; }
}
