using System.Text.RegularExpressions;

namespace Oxira.Web.Domain;

/// <summary>
/// A rule was broken. Reason is the short code the website switches on (invalid, exists, weak, code, wait …);
/// Extra carries numbers the message needs (seconds for wait, minutes for locked).
/// </summary>
public class DomainException(string message, string reason = "invalid", int status = 400, object? extra = null) : Exception(message)
{
    public string Reason { get; } = reason;
    public int Status { get; } = status;
    public object? Extra { get; } = extra;

    public static DomainException NotFound(string message = "غير موجود", string reason = "not_found") => new(message, reason, 404);
    public static DomainException Limit(string message = "محاولات كثيرة، انتظر قليلًا ثم حاول", string reason = "limit") => new(message, reason, 429);
    public static DomainException Session(string message = "انتهت الجلسة، سجّل الدخول من جديد") => new(message, "session", 401);
    public static DomainException Forbidden(string message = "لا تملك صلاحية هذه العملية", string reason = "forbidden") => new(message, reason, 403);
}

public static class Langs
{
    public const string Default = "ar";
    public static readonly string[] All = ["ar", "en", "de", "fr", "ru"];
    public static string Of(string? lang) => lang != null && All.Contains(lang.Trim().ToLowerInvariant()) ? lang.Trim().ToLowerInvariant() : Default;
    public static bool IsAr(string? lang) => Of(lang) == "ar";
}

/// <summary>Oxira account roles. staff and admin are the Oxira team.</summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string Staff = "staff";
    public const string Admin = "admin";
    public static readonly string[] All = [Customer, Staff, Admin];
    public static readonly string[] Team = [Staff, Admin];
    public static bool IsStaff(string? role) => role is Staff or Admin;
}

/// <summary>Roles of a team member on someone else's account. The account's own user is the owner.</summary>
public static class MemberRoles
{
    public const string Owner = "owner";
    public const string Full = "full";
    public const string Viewer = "viewer";
    public static bool Valid(string? r) => r is Full or Viewer;
}

/// <summary>Saudi Arabia keeps UTC+3 all year (no daylight saving).</summary>
public static class Riyadh
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);
    public static DateTimeOffset Now(TimeProvider clock) => clock.GetUtcNow().ToOffset(Offset);
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Now(clock).DateTime);
    public static string Month(TimeProvider clock) => Now(clock).ToString("yyyy-MM");
}

public static partial class Text
{
    /// <summary>trim, drop control characters (keeps new lines), cut to max</summary>
    public static string Clip(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = Controls().Replace(s.Trim(), " ");
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>one line: new lines become spaces</summary>
    public static string Line(string? s, int max) => Clip(s?.Replace('\r', ' ').Replace('\n', ' '), max);

    public static bool IsEmail(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 160 && EmailRe().IsMatch(s.Trim());
    public static string Email(string? s) => (s ?? "").Trim().ToLowerInvariant();

    /// <summary>7 to 15 digits once spaces, dashes and the leading + are removed</summary>
    public static bool IsPhone(string? s) => !string.IsNullOrWhiteSpace(s) && Digits(s).Length is >= 7 and <= 15;

    /// <summary>ASCII digits only; Arabic-Indic digits are converted first</summary>
    public static string Digits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = s.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) : c);
        return new string(chars.Where(char.IsAsciiDigit).ToArray());
    }

    public static string SessionKey(string? sid) => Clip(SafeId().Replace(sid ?? "", ""), 64);

    public static string Html(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "").Replace("\n", "<br>");

    /// <summary>at least 8 characters with a letter (Latin or Arabic) and a digit</summary>
    public static bool IsStrongPassword(string? p) => p != null && PasswordRe().IsMatch(p);

    public static string Hex(int bytes) => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")] private static partial Regex Controls();
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$")] private static partial Regex EmailRe();
    [GeneratedRegex(@"[^A-Za-z0-9_-]")] private static partial Regex SafeId();
    [GeneratedRegex(@"^(?=.*[A-Za-z؀-ۿ])(?=.*\d).{8,128}$", RegexOptions.Singleline)] private static partial Regex PasswordRe();
}
