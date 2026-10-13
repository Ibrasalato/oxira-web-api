using System.Net;
using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Emails;

/// <summary>
/// Email bodies (Arabic for "ar", English otherwise). One simple, table-free layout that reads well in Gmail, Outlook and Zoho.
/// Every value from a visitor goes through E() (HTML-encoded).
/// </summary>
public static partial class Templates
{
    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    public static string Multi(string? s) => E(s).Replace("\n", "<br>");
    private static bool Ar(string lang) => Langs.IsAr(lang);

    public static string Layout(string lang, string title, string body, string? button = null, string? link = null)
    {
        var ar = Ar(lang);
        var btn = button != null && link != null
            ? $"<p style=\"margin:24px 0\"><a href=\"{E(link)}\" style=\"background:#FFC400;color:#0A253E;text-decoration:none;font-weight:700;padding:12px 22px;border-radius:10px;display:inline-block\">{E(button)}</a></p>"
            : "";
        return $"""
            <!doctype html><html lang="{(ar ? "ar" : "en")}" dir="{(ar ? "rtl" : "ltr")}"><meta charset="utf-8">
            <body style="margin:0;background:#F4F6F9;font-family:Tahoma,Arial,sans-serif;color:#0A253E">
            <div style="max-width:560px;margin:0 auto;padding:28px 18px">
            <p style="font-size:22px;font-weight:800;margin:0 0 18px">Oxira <span style="color:#F5A800">●</span></p>
            <div style="background:#fff;border-radius:16px;padding:26px 24px;line-height:1.7;font-size:15px">
            <h1 style="font-size:19px;margin:0 0 12px">{E(title)}</h1>
            {body}{btn}
            </div>
            <p style="font-size:12px;color:#8A98A8;margin:16px 4px">{(ar ? "أوكسيرا لتقنية المعلومات · oxira.sa" : "Oxira IT · oxira.sa")}</p>
            </div></body></html>
            """;
    }

    public static string Rows(params (string Label, string? Value)[] rows) =>
        "<table style=\"width:100%;border-collapse:collapse;font-size:14px\">" +
        string.Concat(rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r =>
            $"<tr><td style=\"padding:6px 0;color:#556779;vertical-align:top;width:34%\">{E(r.Label)}</td><td style=\"padding:6px 0\">{Multi(r.Value)}</td></tr>")) +
        "</table>";

    // ───── account ─────
    public static EmailMessage Code(string to, string code, string purpose, string lang)
    {
        var ar = Ar(lang);
        var (title, why) = purpose switch
        {
            CodePurposes.Login => ar ? ("رمز تسجيل الدخول", "طلبت الدخول إلى حسابك في أوكسيرا. أدخل هذا الرمز لإكمال الدخول.") : ("Your sign-in code", "You asked to sign in to your Oxira account. Enter this code to finish signing in."),
            CodePurposes.Reset => ar ? ("إعادة تعيين كلمة المرور", "طلبت تغيير كلمة مرور حسابك في أوكسيرا. أدخل هذا الرمز ثم اختر كلمة مرور جديدة.") : ("Reset your password", "You asked to reset your Oxira password. Enter this code, then choose a new password."),
            _ => ar ? ("تأكيد بريدك الإلكتروني", "مرحبًا بك في أوكسيرا. أدخل هذا الرمز لتأكيد بريدك وتفعيل حسابك.") : ("Confirm your email", "Welcome to Oxira. Enter this code to confirm your email and activate your account."),
        };
        var body = $"<p>{E(why)}</p><p style=\"font-size:30px;letter-spacing:8px;font-weight:800;margin:18px 0\" dir=\"ltr\">{E(code)}</p>" +
                   $"<p style=\"color:#556779;font-size:13px\">{(ar ? "الرمز صالح لمدة 15 دقيقة. إذا لم تطلبه فتجاهل هذه الرسالة." : "The code is valid for 15 minutes. If you did not ask for it, ignore this email.")}</p>";
        return new EmailMessage([to], $"{code} · {title}", Layout(lang, title, body));
    }

    public static EmailMessage Magic(string to, string link, string lang)
    {
        var ar = Ar(lang);
        var title = ar ? "رابط الدخول إلى حسابك" : "Your sign-in link";
        var body = $"<p>{(ar ? "اضغط الزر للدخول إلى حسابك في أوكسيرا. الرابط يعمل مرة واحدة وصالح 30 دقيقة." : "Press the button to sign in to your Oxira account. The link works once and is valid for 30 minutes.")}</p>" +
                   $"<p style=\"color:#556779;font-size:13px\">{(ar ? "إذا لم تطلب الدخول فتجاهل هذه الرسالة." : "If you did not ask to sign in, ignore this email.")}</p>";
        return new EmailMessage([to], title + " · Oxira", Layout(lang, title, body, ar ? "تسجيل الدخول" : "Sign in", link));
    }

    public static EmailMessage PasswordChanged(string to, string lang)
    {
        var ar = Ar(lang);
        var title = ar ? "تم تغيير كلمة المرور" : "Your password was changed";
        var body = $"<p>{(ar ? "تم تغيير كلمة مرور حسابك وتسجيل الخروج من الأجهزة الأخرى. إذا لم تكن أنت، تواصل معنا فورًا." : "Your password was changed and other devices were signed out. If this was not you, contact us right away.")}</p>";
        return new EmailMessage([to], title + " · Oxira", Layout(lang, title, body));
    }

    public static EmailMessage TeamInvite(string to, string ownerName, string ownerEmail, string role, string link, string lang)
    {
        var ar = Ar(lang);
        var title = ar ? "دعوة للانضمام إلى حساب في أوكسيرا" : "You're invited to an Oxira account";
        var r = role == MemberRoles.Full ? (ar ? "صلاحية كاملة" : "full access") : (ar ? "اطلاع فقط" : "view only");
        var who = string.IsNullOrWhiteSpace(ownerName) ? ownerEmail : $"{ownerName} ({ownerEmail})";
        var body = ar
            ? $"<p>دعاك <b>{E(who)}</b> للعمل على حسابه في أوكسيرا ({E(r)}). أنشئ حسابك أو سجّل الدخول بهذا البريد، ثم اختر الحساب من قائمة الحسابات.</p>"
            : $"<p><b>{E(who)}</b> invited you to work on their Oxira account ({E(r)}). Create an account or sign in with this email, then pick the account from the account switcher.</p>";
        return new EmailMessage([to], title, Layout(lang, title, body, ar ? "افتح حسابي" : "Open my account", link));
    }
}
