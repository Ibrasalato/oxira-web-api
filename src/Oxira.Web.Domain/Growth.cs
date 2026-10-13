namespace Oxira.Web.Domain;

public static class SeoKinds
{
    public static readonly string[] All = ["meta", "article", "local", "gbp", "llms"];
}

/// <summary>A suggestion from the SEO add-on waiting for the owner: page title/description, article, local page, Google Business post, llms.txt.</summary>
public class SeoItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = "article";
    public string Lang { get; set; } = Langs.Default;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Body { get; set; } = "";
    public string Slug { get; set; } = "";
    /// <summary>pending | published | rejected</summary>
    public string Status { get; set; } = "pending";
    public string Url { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>SEO add-on settings and reports of one site.</summary>
public class SeoProfile
{
    public int OrderId { get; set; }
    /// <summary>{services, areas, audience, usp, notes, keywords[], competitors[], langs[]}</summary>
    public string BriefJson { get; set; } = "{}";
    /// <summary>{maps, manager}</summary>
    public string GbpJson { get; set; } = "{}";
    public string WpUrl { get; set; } = "";
    public string WpUser { get; set; } = "";
    /// <summary>the WordPress application password, encrypted with ASP.NET Data Protection</summary>
    public string WpSecret { get; set; } = "";
    public bool WpOk { get; set; }
    /// <summary>reports filled by the team or a reporting job (Search Console, PageSpeed): JSON, null until there is data</summary>
    public string? ReportJson { get; set; }
    public string HistoryJson { get; set; } = "[]";
    public string RanksJson { get; set; } = "{\"data\":[],\"weeks\":[]}";
    public string? HealthJson { get; set; }
    public DateTimeOffset? LastDraftAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Social media add-on settings of one site: the brief and the Meta (Facebook / Instagram) connection.</summary>
public class SocialProfile
{
    public int OrderId { get; set; }
    /// <summary>{about, audience, offers, areas, avoid, voice, lang, time, platforms[], photos[], competitors[], replies, ads_cap, ads_goal, age_min, age_max, special}</summary>
    public string BriefJson { get; set; } = "{}";
    /// <summary>what the page shows: {connected, connected_at, user_name, pages[{id,name,ig_username}], adaccounts[{id,name,currency}], page_id, ad_account_id, ig_username}</summary>
    public string MetaJson { get; set; } = "{}";
    /// <summary>Meta access tokens (user and pages), encrypted with ASP.NET Data Protection</summary>
    public string MetaSecret { get; set; } = "";
    public string? ReportJson { get; set; }
    public string LastPlanMonth { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SocialStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Published = "published";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    /// <summary>a reel the owner filmed</summary>
    public const string Done = "done";
}

/// <summary>One item of a monthly social plan: post, reel, ad, or an automatic reply (log).</summary>
public class SocialItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = "";
    /// <summary>post | reel | ad | reply</summary>
    public string Kind { get; set; } = "post";
    public string Status { get; set; } = SocialStatuses.Pending;
    public string Title { get; set; } = "";
    public string Caption { get; set; } = "";
    public string Hashtags { get; set; } = "";
    /// <summary>yyyy-MM-dd and HH:mm (Riyadh)</summary>
    public string Day { get; set; } = "";
    public string Time { get; set; } = "19:00";
    public string Platforms { get; set; } = "instagram,facebook";
    /// <summary>background image id (uploaded or generated)</summary>
    public string Bg { get; set; } = "";
    /// <summary>the final design (image id) approved by the owner</summary>
    public string Image { get; set; } = "";
    public string Idea { get; set; } = "";
    /// <summary>ad settings: {goal, cities[], age_min, age_max, interests[], budget, days, text, headline, link, post_id, post_title, post_day, why}</summary>
    public string AdJson { get; set; } = "{}";
    /// <summary>publishing result: {fb_id, ig_id, error, end}</summary>
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One uptime check of a public service (status page).</summary>
public class StatusCheck
{
    public long Id { get; set; }
    /// <summary>web | events | design | api | automation</summary>
    public string Key { get; set; } = "";
    public bool Ok { get; set; }
    public int Ms { get; set; }
    public DateTimeOffset At { get; set; }
}
