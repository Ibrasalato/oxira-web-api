using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>"Anthropic" section; the key comes from an environment variable (Anthropic__ApiKey).</summary>
public class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model { get; set; } = "claude-sonnet-5";
}

/// <summary>Claude Messages API (https://docs.anthropic.com/en/api/messages).</summary>
public class AnthropicClient(HttpClient http, IOptions<AnthropicOptions> opt) : IAnthropic
{
    public bool Enabled => !string.IsNullOrEmpty(opt.Value.ApiKey);

    public async Task<AiResponse> CreateMessageAsync(AiRequest r, CancellationToken ct = default)
    {
        var o = opt.Value;
        if (!Enabled) throw new InvalidOperationException("Anthropic:ApiKey is not set");
        var body = new JsonObject
        {
            ["model"] = r.Model ?? o.Model,
            ["max_tokens"] = r.MaxTokens,
            ["system"] = r.System,
            ["messages"] = r.Messages.DeepClone(),
        };
        if (r.Tools is { Count: > 0 }) body["tools"] = r.Tools.DeepClone();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/v1/messages") { Content = JsonContent.Create(body) };
        req.Headers.Add("x-api-key", o.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Anthropic {(int)res.StatusCode}: {text[..Math.Min(text.Length, 400)]}");
        var json = JsonNode.Parse(text)!.AsObject();
        return new AiResponse(json["content"]?.AsArray() ?? [], (string?)json["stop_reason"] ?? "end_turn");
    }
}
