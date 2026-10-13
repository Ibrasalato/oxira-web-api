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

public static partial class Templates
{
    public static EmailMessage TeamAdApproved(Order o, SocialItem item, string team)
    {
        var title = $"إعلان معتمد للإطلاق: {o.SiteName} (طلب {o.Id})";
        var body = "<p>اعتمد العميل الإعلان التالي. أطلقه من مدير إعلانات ميتا على حسابه الإعلاني المختار.</p>" +
                   Rows(("العنوان", item.Title), ("الإعداد", item.AdJson), ("الموقع", o.Url), ("الحساب", o.Email));
        return new EmailMessage([team], title, Layout("ar", title, body), IsMail(o.Email));
    }
}

public static partial class Templates
{
    public static EmailMessage RenewalDue(string to, Subscription s, string site, string payUrl, decimal total, string lang)
    {
        var ar = Ar(lang);
        var title = ar ? $"تجديد {s.Product} لموقع {site}" : $"Renewal of {s.Product} for {site}";
        var body = ar
            ? $"<p>يحين تجديد الخدمة في <b dir=\"ltr\">{s.NextDue:yyyy-MM-dd}</b>. المبلغ <b>{Sar(total)}</b> شامل الضريبة.</p><p>إذا لم يتم الدفع تتوقف الخدمة بعد 7 أيام من تاريخ التجديد.</p>"
            : $"<p>The service renews on <b>{s.NextDue:yyyy-MM-dd}</b>. The amount is <b>{Sar(total)}</b> including VAT.</p><p>If it is not paid, the service stops 7 days after the renewal date.</p>";
        return new EmailMessage([to], title + " · Oxira", Layout(lang, title, body, ar ? "ادفع الآن" : "Pay now", payUrl));
    }

    public static EmailMessage SiteMessageToOwner(Order o, SiteMessage m)
    {
        var ar = Ar(o.Lang);
        var title = ar ? $"رسالة جديدة من موقعك {o.SiteName}" : $"New message from your site {o.SiteName}";
        return new EmailMessage([o.Email], title, Layout(o.Lang, title, Rows((ar ? "الاسم" : "Name", m.Name), (ar ? "الجوال" : "Phone", m.Phone), (ar ? "الرسالة" : "Message", m.Message))));
    }

    public static EmailMessage SiteBookingToOwner(Order o, SiteBooking b)
    {
        var ar = Ar(o.Lang);
        var title = ar ? $"حجز جديد {b.Date} {b.Time}: {b.Name}" : $"New booking {b.Date} {b.Time}: {b.Name}";
        var body = Rows((ar ? "الخدمة" : "Service", b.Service), (ar ? "الموعد" : "When", $"{b.Date} {b.Time}"), (ar ? "الاسم" : "Name", b.Name), (ar ? "الجوال" : "Phone", b.Phone),
                        (ar ? "البريد" : "Email", b.Email), (ar ? "ملاحظة" : "Note", b.Note)) +
                   (ar ? "<p>أكّد الحجز من حسابك في أوكسيرا.</p>" : "<p>Confirm it in your Oxira account.</p>");
        return new EmailMessage([o.Email], title, Layout(o.Lang, title, body), IsMail(b.Email));
    }

    public static EmailMessage SiteBookingToClient(Order o, SiteBooking b)
    {
        var ar = Ar(b.Lang);
        var title = ar ? $"طلب حجزك لدى {o.SiteName}" : $"Your booking request at {o.SiteName}";
        var body = ar ? $"<p>مرحبًا {E(b.Name)}، وصل طلب حجزك يوم <b dir=\"ltr\">{b.Date}</b> الساعة <b dir=\"ltr\">{b.Time}</b> ({E(b.Service)}). سيؤكده {E(o.SiteName)} قريبًا.</p>"
                      : $"<p>Hi {E(b.Name)}, your booking request for <b>{b.Date}</b> at <b>{b.Time}</b> ({E(b.Service)}) was received. {E(o.SiteName)} will confirm it soon.</p>";
        return new EmailMessage([b.Email], title, Layout(b.Lang, title, body));
    }

    public static EmailMessage TicketIssued(Order o, EventTicket t, string link)
    {
        var ar = Ar(t.Lang);
        var title = ar ? $"تذكرتك {t.Code} · {o.SiteName}" : $"Your ticket {t.Code} · {o.SiteName}";
        var body = ar ? $"<p>مرحبًا {E(t.Name)}، تذكرتك ({E(t.TypeName)} × {t.Qty}) جاهزة. اعرض رمز QR من الرابط عند الدخول.</p>"
                      : $"<p>Hi {E(t.Name)}, your ticket ({E(t.TypeName)} × {t.Qty}) is ready. Show the QR code from the link at the entrance.</p>";
        return new EmailMessage([t.Email], title, Layout(t.Lang, title, body, ar ? "عرض التذكرة" : "Show the ticket", link));
    }

    public static EmailMessage SiteReviewToOwner(Order o, SiteReview r)
    {
        var ar = Ar(o.Lang);
        var title = ar ? $"تقييم {r.Rating}/5 على موقعك {o.SiteName}" : $"{r.Rating}/5 rating on your site {o.SiteName}";
        return new EmailMessage([o.Email], title, Layout(o.Lang, title, Rows((ar ? "التقييم" : "Rating", $"{r.Rating}/5"), (ar ? "الاسم" : "Name", r.Name), (ar ? "التعليق" : "Comment", r.Comment))));
    }
}
