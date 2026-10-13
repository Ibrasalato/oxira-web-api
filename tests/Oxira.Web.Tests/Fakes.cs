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
