using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Common;

public partial interface IAppDbContext
{
    DbSet<Lead> Leads { get; }
    DbSet<Partner> Partners { get; }
    DbSet<Testimonial> Testimonials { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<AuditRun> AuditRuns { get; }
    DbSet<AgentDemo> AgentDemos { get; }
    DbSet<AgentSubscription> AgentSubscriptions { get; }
}

// ───── AI ─────
/// <summary>One call to Anthropic's Messages API. Content and tool inputs are raw JSON so tools can be described freely.</summary>
public record AiRequest(string System, JsonArray Messages, JsonArray? Tools = null, int MaxTokens = 900, string? Model = null);
public record AiResponse(JsonArray Content, string StopReason)
{
    public string Text => string.Join("\n", Content.OfType<JsonObject>().Where(b => (string?)b["type"] == "text").Select(b => (string?)b["text"])).Trim();
}

public interface IAnthropic
{
    /// <summary>false when no API key is configured (features then answer "unavailable")</summary>
    bool Enabled { get; }
    Task<AiResponse> CreateMessageAsync(AiRequest request, CancellationToken ct = default);
}

// ───── fetching visitors' websites (SSRF-safe) ─────
public record FetchResult(bool Ok, int Status, string FinalUrl, string? ContentType, string Body, long Bytes, int Ms, string? Error = null);

/// <summary>
/// GETs a public web page for the site audit and the agent demo: http/https only, public IP addresses only (checked when connecting,
/// so DNS tricks and redirects to internal hosts fail), at most a few redirects, a size cap and a time limit.
/// </summary>
public interface ISafeFetcher
{
    Task<FetchResult> GetAsync(Uri url, int maxBytes = 2_000_000, CancellationToken ct = default);
}

/// <summary>"Ai" section.</summary>
public class AiOptions
{
    public string Model { get; set; } = "claude-sonnet-5";
    public int MaxTokens { get; set; } = 900;
    public int MaxIterations { get; set; } = 6;
    /// <summary>visible turns kept as memory</summary>
    public int Memory { get; set; } = 14;
    public int MessagesPerSession { get; set; } = 60;
    public int MessagesPerDay { get; set; } = 3000;
}

/// <summary>"Agent" section: WhatsApp agent demos.</summary>
public class AgentOptions
{
    public int MessagesPerDemo { get; set; } = 30;
    public int DemosPerIp { get; set; } = 5;
    public int DemosPerEmail { get; set; } = 3;
    public int DemosPerDay { get; set; } = 200;
    /// <summary>messages per visitor per day on the example demos</summary>
    public int ExampleMessagesPerIp { get; set; } = 20;
}

/// <summary>"Audit" section.</summary>
public class AuditOptions
{
    public int PerIp { get; set; } = 10;
    public int PerDay { get; set; } = 400;
}
