using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Emails;

public static partial class Templates
{
    private static readonly Dictionary<string, string> KindNames = new()
    {
        [LeadKinds.Project] = "طلب مشروع", [LeadKinds.Support] = "دعم فني", [LeadKinds.Chat] = "من المحادثة الذكية",
        [LeadKinds.Partner] = "شريك جديد", [LeadKinds.Audit] = "فحص موقع", [LeadKinds.Agent] = "تجربة وكيل واتساب",
    };

    /// <summary>to the team: any website lead</summary>
    public static EmailMessage TeamLead(Lead l, string team, string? extraHtml = null)
    {
        var title = $"{KindNames.GetValueOrDefault(l.Kind, l.Kind)} رقم {l.Id}: {l.Name}";
        var body = Rows(("الاسم", l.Name), ("البريد", l.Email), ("الجوال", l.Phone), ("الجهة", l.Company), ("الخدمة", l.Service),
                        ("التفاصيل", l.Details), ("اللغة", l.Lang), ("الصفحة", l.Page)) + (extraHtml ?? "");
        return new EmailMessage([team], title, Layout("ar", title, body), Text.IsEmail(l.Email) ? l.Email : null);
    }

    /// <summary>to the visitor: we got your message</summary>
    public static EmailMessage LeadThanks(Lead l, string whatsapp)
    {
        var ar = Ar(l.Lang);
        var title = ar ? "وصلتنا رسالتك" : "We received your message";
        var body = ar
            ? $"<p>مرحبًا {E(l.Name)}،</p><p>شكرًا لتواصلك مع أوكسيرا. رقم طلبك <b>{l.Id}</b>، وسيرد عليك فريقنا خلال يوم عمل.</p><p>للتواصل السريع: واتساب <span dir=\"ltr\">{E(whatsapp)}</span></p>"
            : $"<p>Hi {E(l.Name)},</p><p>Thanks for contacting Oxira. Your request number is <b>{l.Id}</b>; our team will reply within one working day.</p><p>For a quick chat: WhatsApp <span dir=\"ltr\">{E(whatsapp)}</span></p>";
        return new EmailMessage([l.Email], title + " · Oxira", Layout(l.Lang, title, body));
    }

    public static EmailMessage PartnerWelcome(Partner p, string link)
    {
        var ar = Ar(p.Lang);
        var title = ar ? "أهلًا بك شريكًا في أوكسيرا" : "Welcome to the Oxira partner programme";
        var pct = (int)Math.Round(p.Rate * 100);
        var body = ar
            ? $"<p>مرحبًا {E(p.Name)}،</p><p>رابطك الخاص:</p><p dir=\"ltr\"><b>{E(link)}</b></p><p>كل عميل يطلب موقعًا أو خدمة عبر رابطك خلال 90 يومًا تحصل على {pct}% من قيمة طلبه بعد الدفع. تتابع إحالاتك وعمولتك من حسابك في أوكسيرا.</p>"
            : $"<p>Hi {E(p.Name)},</p><p>Your partner link:</p><p dir=\"ltr\"><b>{E(link)}</b></p><p>For every client who orders through your link within 90 days you earn {pct}% of their paid order. Track referrals and commission in your Oxira account.</p>";
        return new EmailMessage([p.Email], title, Layout(p.Lang, title, body));
    }

    public static EmailMessage TeamTestimonial(Testimonial t, string team)
    {
        var title = $"رأي عميل جديد بانتظار المراجعة ({t.Rating}/5): {t.Name}";
        var body = Rows(("الاسم", t.Name), ("البريد", t.Email), ("المسمى", t.Role), ("الجهة", t.Company), ("التقييم", $"{t.Rating}/5"), ("الرأي", t.Text), ("اللغة", t.Lang)) +
                   "<p style=\"color:#556779;font-size:13px\">لا يظهر في الموقع إلا بعد الموافقة عليه من لوحة الفريق (PUT /api/admin/testimonials/{id}).</p>";
        return new EmailMessage([team], title, Layout("ar", title, body), Text.IsEmail(t.Email) ? t.Email : null);
    }
}

public static partial class Templates
{
    public static EmailMessage AuditReport(string to, string name, Features.Audit.AuditResult r, string lang, SiteOptions site)
    {
        var ar = Ar(lang);
        var title = ar ? $"نتيجة فحص موقعك {r.Host}: {r.Score}/100" : $"Your website check for {r.Host}: {r.Score}/100";
        var labels = ar
            ? new Dictionary<string, string> { ["reachable"] = "الموقع يعمل", ["https"] = "اتصال آمن HTTPS", ["title"] = "عنوان الصفحة", ["description"] = "وصف الصفحة", ["h1"] = "عنوان رئيسي H1", ["viewport"] = "مناسب للجوال", ["lang"] = "لغة الصفحة", ["canonical"] = "الرابط الأساسي", ["og"] = "معاينة المشاركة", ["schema"] = "بيانات منظمة", ["alt"] = "وصف الصور", ["robots"] = "ملف robots.txt", ["sitemap"] = "خريطة الموقع", ["size"] = "حجم الصفحة" }
            : new Dictionary<string, string> { ["reachable"] = "Site is up", ["https"] = "Secure HTTPS", ["title"] = "Page title", ["description"] = "Meta description", ["h1"] = "Main heading (H1)", ["viewport"] = "Mobile friendly", ["lang"] = "Page language", ["canonical"] = "Canonical link", ["og"] = "Share preview", ["schema"] = "Structured data", ["alt"] = "Image alt text", ["robots"] = "robots.txt", ["sitemap"] = "Sitemap", ["size"] = "Page size" };
        var list = string.Concat(r.Checks.OrderBy(c => c.Ok).Select(c => $"<li style=\"margin:4px 0\">{(c.Ok ? "✅" : "⚠️")} {E(labels.GetValueOrDefault(c.Id, c.Id))}</li>"));
        var body = (ar ? $"<p>مرحبًا {E(name)}،</p><p>هذه نتيجة الفحص المجاني لموقع <b dir=\"ltr\">{E(r.Host)}</b>:</p>" : $"<p>Hi {E(name)},</p><p>Here is the free check of <b>{E(r.Host)}</b>:</p>") +
                   Rows((ar ? "السرعة" : "Performance", r.Scores.Performance.ToString()), ("SEO", r.Scores.Seo.ToString()),
                        (ar ? "سهولة الوصول" : "Accessibility", r.Scores.Accessibility.ToString()), (ar ? "أفضل الممارسات" : "Best practices", r.Scores.BestPractices.ToString())) +
                   $"<ul style=\"padding-inline-start:18px\">{list}</ul>" +
                   (ar ? "<p>نقدر نصلح لك كل الملاحظات ونرفع ظهور موقعك في Google. رد على هذه الرسالة أو تواصل معنا.</p>" : "<p>We can fix every item and lift your site in Google. Reply to this email or contact us.</p>");
        return new EmailMessage([to], title, Layout(lang, title, body, ar ? "تواصل معنا" : "Contact us", $"{site.SiteUrl.TrimEnd('/')}{(ar ? "" : "/en")}/contact/?service=website-audit"));
    }
}

public static partial class Templates
{
    public static EmailMessage AgentDemoReady(string to, string business, string link, string lang)
    {
        var ar = Ar(lang);
        var title = ar ? $"وكيل واتساب لـ {business} جاهز للتجربة" : $"Your WhatsApp agent for {business} is ready";
        var body = ar
            ? "<p>جهّزنا لك تجربة وكيل ذكي يرد على عملائك من معلومات موقعك. جرّبه واسأله مثل ما يسأل عملاؤك، وشارك الرابط مع فريقك.</p>"
            : "<p>We set up a demo AI agent that answers your customers from your website's information. Try it like a customer would, and share the link with your team.</p>";
        return new EmailMessage([to], title, Layout(lang, title, body, ar ? "جرّب الوكيل" : "Try the agent", link));
    }

    public static EmailMessage AgentSubscription(AgentSubscription s, string demoLink, string team)
    {
        var title = $"طلب اشتراك وكيل واتساب ({s.Plan}) رقم {s.Id}: {s.Business}";
        var body = Rows(("الباقة", s.Plan), ("الاسم", s.Name), ("النشاط", s.Business), ("البريد", s.Email), ("الجوال", s.Phone), ("واتساب النشاط", s.Whatsapp),
                        ("المدينة", s.City), ("ملاحظات", s.Notes), ("التجربة", demoLink), ("اللغة", s.Lang));
        return new EmailMessage([team], title, Layout("ar", title, body), s.Email);
    }

    public static EmailMessage AgentSubscriptionThanks(AgentSubscription s, string whatsapp)
    {
        var ar = Ar(s.Lang);
        var title = ar ? "وصلنا طلب اشتراكك في وكيل واتساب" : "We received your WhatsApp agent request";
        var body = ar
            ? $"<p>مرحبًا {E(s.Name)}،</p><p>رقم طلبك <b>{s.Id}</b>. سيتواصل معك فريقنا خلال يوم عمل لربط الوكيل برقم واتساب {E(s.Business)} وضبط ردوده.</p><p>واتساب: <span dir=\"ltr\">{E(whatsapp)}</span></p>"
            : $"<p>Hi {E(s.Name)},</p><p>Your request number is <b>{s.Id}</b>. Our team will contact you within one working day to connect the agent to {E(s.Business)}'s WhatsApp number and fine-tune its replies.</p><p>WhatsApp: <span dir=\"ltr\">{E(whatsapp)}</span></p>";
        return new EmailMessage([s.Email], title, Layout(s.Lang, title, body));
    }
}
