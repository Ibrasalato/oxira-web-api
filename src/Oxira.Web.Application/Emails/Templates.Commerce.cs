using System.Globalization;
using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Emails;

public static partial class Templates
{
    public static string Sar(decimal v) => v.ToString("#,0.##", CultureInfo.InvariantCulture) + " SAR";

    private static (string Label, string? Value)[] OrderRows(Order o) =>
    [
        ("رقم الطلب", o.Id.ToString()), ("النوع", o.Kind), ("الاسم", o.Name), ("البريد", o.Email), ("الجوال", o.Phone), ("اسم الموقع / النشاط", o.SiteName),
        ("التصميم", o.Template), ("لغة الموقع", o.SiteLang), ("الباقة", o.Plan), ("الدومين", o.DomainOption == "none" ? "" : $"{o.DomainName} ({o.DomainOption})"),
        ("الإضافات", o.Addons), ("الإجمالي", $"{Sar(o.Subtotal)} + {Sar(o.Vat)} = {Sar(o.Total)}"), ("رمز الشريك", o.Ref), ("ملاحظات", o.Notes), ("تفاصيل", o.MetaJson == "{}" ? "" : o.MetaJson),
    ];

    public static EmailMessage TeamOrder(Order o, string team)
    {
        var title = $"طلب جديد رقم {o.Id} ({o.Kind}): {o.SiteName} — بانتظار الدفع";
        return new EmailMessage([team], title, Layout("ar", title, Rows(OrderRows(o))), IsMail(o.Email));
    }

    public static EmailMessage OrderReceived(Order o)
    {
        var ar = Ar(o.Lang);
        var title = ar ? $"استلمنا طلبك رقم {o.Id}" : $"We received your order #{o.Id}";
        var body = ar
            ? $"<p>مرحبًا {E(o.Name)}،</p><p>شكرًا لطلبك من أوكسيرا. الإجمالي <b>{Sar(o.Total)}</b> شامل ضريبة القيمة المضافة. إذا لم تكمل الدفع، سيرسل لك فريقنا رابط الدفع ويتواصل معك خلال يوم عمل.</p>"
            : $"<p>Hi {E(o.Name)},</p><p>Thank you for your Oxira order. The total is <b>{Sar(o.Total)}</b> including VAT. If you did not finish paying, our team will send you a payment link and contact you within one working day.</p>";
        return new EmailMessage([o.Email], title, Layout(o.Lang, title, body));
    }

    public static EmailMessage TeamOrderPaid(Order o, string invoice, string team)
    {
        var title = $"تم الدفع ✅ طلب {o.Id}: {o.SiteName} ({Sar(o.Total)})";
        // the only place a domain registration is requested: the client paid for it and the payment is confirmed
        var domain = o.DomainPaid && o.DomainName != ""
            ? $"<p style=\"background:#FFF6D6;padding:10px 12px;border-radius:10px\"><b>سجّل الدومين:</b> <span dir=\"ltr\">{E(o.DomainName)}</span> — دفع العميل قيمته وتأكد الدفع.</p>"
            : o.DomainOption == "own" && o.DomainName != "" ? $"<p>العميل يملك الدومين <span dir=\"ltr\">{E(o.DomainName)}</span>: أرسل له إعدادات DNS.</p>" : "";
        var body = $"<p>الفاتورة: <b dir=\"ltr\">{E(invoice)}</b> · الموقع: <span dir=\"ltr\">{E(o.Url)}</span></p>" + domain + Rows(OrderRows(o));
        return new EmailMessage([team], title, Layout("ar", title, body), IsMail(o.Email));
    }

    public static EmailMessage OrderPaid(Order o, string invoice, SiteOptions site)
    {
        var ar = Ar(o.Lang);
        var title = ar ? $"تم الدفع، شكرًا لك! طلب رقم {o.Id}" : $"Payment received, thank you! Order #{o.Id}";
        var live = o.Url != "" && o.Status == OrderStatuses.Published;
        var body = ar
            ? $"<p>مرحبًا {E(o.Name)}،</p><p>وصلنا مبلغ <b>{Sar(o.Total)}</b> (فاتورة <span dir=\"ltr\">{E(invoice)}</span>).</p>" +
              (live ? $"<p>موقعك منشور على <a href=\"{E(o.Url)}\" dir=\"ltr\">{E(o.Url)}</a>.</p>" : "<p>يبدأ فريقنا تجهيز موقعك الآن وسنرسل لك الرابط قريبًا.</p>") +
              "<p>تدير موقعك وإضافاته وفواتيرك من حسابك في أوكسيرا بهذا البريد (إذا لم يكن لديك كلمة مرور، اختر «نسيت كلمة المرور»).</p>"
            : $"<p>Hi {E(o.Name)},</p><p>We received <b>{Sar(o.Total)}</b> (invoice <span dir=\"ltr\">{E(invoice)}</span>).</p>" +
              (live ? $"<p>Your site is live at <a href=\"{E(o.Url)}\">{E(o.Url)}</a>.</p>" : "<p>Our team is preparing your site now and will send you the link soon.</p>") +
              "<p>Manage your site, add-ons and invoices in your Oxira account with this email (no password yet? choose \"Forgot password\").</p>";
        return new EmailMessage([o.Email], title, Layout(o.Lang, title, body, ar ? "افتح حسابي" : "Open my account", site.Account(o.Lang)));
    }

    private static string? IsMail(string e) => Text.IsEmail(e) ? e : null;
}
