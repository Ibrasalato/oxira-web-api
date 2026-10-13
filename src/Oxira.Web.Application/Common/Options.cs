namespace Oxira.Web.Application.Common;

/// <summary>"Site" section.</summary>
public class SiteOptions
{
    /// <summary>team inbox (leads, orders, tickets)</summary>
    public string TeamEmail { get; set; } = "info@oxira.sa";
    public string SiteUrl { get; set; } = "https://oxira.sa";
    /// <summary>public address of this API (payment callbacks, OAuth redirects, image links)</summary>
    public string ApiUrl { get; set; } = "https://api.oxira.sa";
    public string WhatsApp { get; set; } = "+966 59 669 4021";
    /// <summary>optional n8n webhook that receives a copy of team notifications (the API never depends on it)</summary>
    public string? NotifyWebhook { get; set; }

    public string Account(string lang) => $"{SiteUrl.TrimEnd('/')}{(lang == "ar" ? "" : "/" + lang)}/account/";
}

/// <summary>"Auth" section: sign-in rules.</summary>
public class AuthOptions
{
    /// <summary>a magic link's returnUrl must start with one of these origins</summary>
    public string[] MagicLinkOrigins { get; set; } = [];
    public static readonly string[] DefaultOrigins = ["https://design.oxira.sa", "https://oxira.sa", "https://ibrasalato.github.io", "http://localhost:4321"];
    /// <summary>(configuration binding appends to initialised arrays, so the defaults live here)</summary>
    public string[] Origins => MagicLinkOrigins.Length > 0 ? MagicLinkOrigins : DefaultOrigins;
    public int MagicLinkMinutes { get; set; } = 30;
    /// <summary>emailed 6-digit codes</summary>
    public int CodeMinutes { get; set; } = 15;
    public int CodeAttempts { get; set; } = 5;
    /// <summary>seconds between two codes to the same email, and codes per hour</summary>
    public int ResendSeconds { get; set; } = 60;
    public int CodesPerHour { get; set; } = 6;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockMinutes { get; set; } = 15;
    /// <summary>accounts that are Oxira admins from their first sign-in</summary>
    public string[] AdminEmails { get; set; } = [];
}
