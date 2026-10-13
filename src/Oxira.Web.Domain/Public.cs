namespace Oxira.Web.Domain;

public static class LeadKinds
{
    public const string Project = "project";
    public const string Support = "support";
    public const string Chat = "chat";
    public const string Partner = "partner";
    public const string Audit = "audit";
    public const string Agent = "agent";
}

/// <summary>A request from the website: contact form, AI chat, partner sign-up, site audit or WhatsApp agent demo.</summary>
public class Lead
{
    public int Id { get; set; }
    public string Kind { get; set; } = LeadKinds.Project;
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Company { get; set; } = "";
    public string Service { get; set; } = "";
    public string Details { get; set; } = "";
    public string Lang { get; set; } = Langs.Default;
    public string Page { get; set; } = "";
    public string Ip { get; set; } = "";
    /// <summary>new | contacted | won | lost | spam</summary>
    public string Status { get; set; } = "new";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A referral partner: earns Rate of every paid order that came with their code.</summary>
public class Partner
{
    public int Id { get; set; }
    public Guid? UserId { get; set; }
    public string Name { get; set; } = "";
    public string Company { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>the ?ref= code in partner links</summary>
    public string Code { get; set; } = "";
    public decimal Rate { get; set; } = 0.15m;
    /// <summary>commission already paid out (SAR)</summary>
    public decimal PaidOut { get; set; }
    public string Lang { get; set; } = Langs.Default;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A client review from /feedback/, shown on the site once the team approves it.</summary>
public class Testimonial
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Company { get; set; } = "";
    public int Rating { get; set; } = 5;
    public string Text { get; set; } = "";
    public string Lang { get; set; } = Langs.Default;
    /// <summary>pending | approved | rejected</summary>
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Chat memory: the visible turns of one chat session (oxira.sa assistant, or a client site's assistant).</summary>
public class ChatMessage
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    /// <summary>"oxira" for the oxira.sa assistant, "site:&lt;order id&gt;" for a client site</summary>
    public string Channel { get; set; } = "oxira";
    /// <summary>user | assistant</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string Lang { get; set; } = Langs.Default;
    public string Page { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One free website check.</summary>
public class AuditRun
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public string Host { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public int Score { get; set; }
    public string ResultJson { get; set; } = "{}";
    public string Lang { get; set; } = Langs.Default;
    public string Ip { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A WhatsApp AI agent demo built from a business's website or notes. Examples are fictional, clearly labelled businesses.</summary>
public class AgentDemo
{
    /// <summary>12 hex characters (the ?d= of the demo page)</summary>
    public string Id { get; set; } = "";
    public string Business { get; set; } = "";
    public string Url { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Lang { get; set; } = Langs.Default;
    /// <summary>the agent's instructions (system prompt), written by Claude from the site content</summary>
    public string Profile { get; set; } = "";
    public string Welcome { get; set; } = "";
    public string SuggestionsJson { get; set; } = "[]";
    public bool Example { get; set; }
    public int Messages { get; set; }
    public string Ip { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A request to subscribe to the WhatsApp agent (basic | pro).</summary>
public class AgentSubscription
{
    public int Id { get; set; }
    public string DemoId { get; set; } = "";
    public string Plan { get; set; } = "pro";
    public string Name { get; set; } = "";
    public string Business { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Whatsapp { get; set; } = "";
    public string City { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Lang { get; set; } = Langs.Default;
    public string Status { get; set; } = "new";
    public DateTimeOffset CreatedAt { get; set; }
}
