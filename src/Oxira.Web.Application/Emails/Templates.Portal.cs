using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Emails;

public static partial class Templates
{
    public static EmailMessage SubscriptionPaid(string to, Subscription s, Order o, string invoice, decimal amount, string lang, SiteOptions site)
    {
        var ar = Ar(lang);
        var name = o.SiteName != "" ? o.SiteName : o.DomainName;
        var title = ar ? $"تم تفعيل {s.Product} لموقع {name}" : $"{s.Product} is active for {name}";
        var body = ar
            ? $"<p>وصلنا مبلغ <b>{Sar(amount)}</b> (فاتورة <span dir=\"ltr\">{E(invoice)}</span>). الخدمة فعّالة حتى <b dir=\"ltr\">{s.NextDue:yyyy-MM-dd}</b> وتجدد تلقائيًا بفاتورة جديدة.</p>"
            : $"<p>We received <b>{Sar(amount)}</b> (invoice <span dir=\"ltr\">{E(invoice)}</span>). The service is active until <b>{s.NextDue:yyyy-MM-dd}</b> and renews with a new invoice.</p>";
        return new EmailMessage([to], title + " · Oxira", Layout(lang, title, body, ar ? "افتح حسابي" : "Open my account", site.Account(lang) + "#billing"));
    }

    public static EmailMessage TeamTicket(SupportTicket t, string text, string team)
    {
        var title = $"تذكرة دعم {t.Number}: {t.Subject}";
        return new EmailMessage([team], title, Layout("ar", title, Rows(("الحساب", t.Email), ("التصنيف", t.Category), ("الطلب", t.OrderId?.ToString()), ("الرسالة", text))), IsMail(t.Email));
    }

    public static EmailMessage TicketReply(string to, SupportTicket t, string text, string lang, SiteOptions site)
    {
        var ar = Ar(lang);
        var title = ar ? $"رد فريق أوكسيرا على تذكرتك {t.Number}" : $"Oxira replied to your ticket {t.Number}";
        var body = $"<p><b>{E(t.Subject)}</b></p><p style=\"background:#F4F6F9;padding:12px;border-radius:10px\">{Multi(text)}</p>";
        return new EmailMessage([to], title, Layout(lang, title, body, ar ? "افتح التذكرة" : "Open the ticket", site.Account(lang) + "#support"));
    }
}
