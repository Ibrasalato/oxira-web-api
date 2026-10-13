using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Public;

// ───── inputs (the website's form field names) ─────
public record ContactInput(string? Type, string? Name, string? Email, string? Phone, string? Service, string? Details, string? Lang, string? Page,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record PartnerInput(string? Name, string? Company, string? Phone, string? Email, string? Lang,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record TestimonialInput(string? Name, string? Email, string? Role, string? Company, int? Rating, string? Text, string? Consent, string? Lang,
    [property: JsonPropertyName("company_website")] string? CompanyWebsite);
public record StatusInput(string? Status);

public record SubmitResult(bool Success, int Id);
public record ProofTestimonial(string Name, string Role, string Company, int Rating, string Text, string Lang);
public record ProofDto(int Sites, List<ProofTestimonial> Testimonials);
public record LeadDto(int Id, string Kind, string Name, string Email, string Phone, string Company, string Service, string Details, string Lang, string Page, string Status, DateTimeOffset CreatedAt);
public record TestimonialDto(int Id, string Name, string Email, string Role, string Company, int Rating, string Text, string Lang, string Status, DateTimeOffset CreatedAt);

public record ContactCommand(ContactInput Input) : ICommand<SubmitResult>;
public record PartnerSignupCommand(PartnerInput Input) : ICommand<SubmitResult>;
public record TestimonialCommand(TestimonialInput Input) : ICommand<SubmitResult>;
public record ProofQuery : IQuery<ProofDto>;
public record ListLeadsQuery(string? Kind, int Take = 100) : IQuery<List<LeadDto>>;
public record ListTestimonialsQuery(string? Status) : IQuery<List<TestimonialDto>>;
public record ModerateTestimonialCommand(int Id, string? Status) : ICommand<TestimonialDto>;

/// <summary>Counts shown as social proof: published client sites (see Features/Studio) plus Proof:BaseSites.</summary>
public interface ISiteCounter
{
    Task<int> PublishedSitesAsync(CancellationToken ct);
}

/// <summary>Contact form, partner sign-up, client reviews and the proof strip.</summary>
public class FormHandlers(IAppDbContext db, TimeProvider clock, ICurrentUser current, IEmailSender email, IOptions<SiteOptions> site, Access access, ISiteCounter sites)
    : IRequestHandler<ContactCommand, SubmitResult>, IRequestHandler<PartnerSignupCommand, SubmitResult>, IRequestHandler<TestimonialCommand, SubmitResult>,
      IRequestHandler<ProofQuery, ProofDto>, IRequestHandler<ListLeadsQuery, List<LeadDto>>, IRequestHandler<ListTestimonialsQuery, List<TestimonialDto>>,
      IRequestHandler<ModerateTestimonialCommand, TestimonialDto>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<SubmitResult> Handle(ContactCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new SubmitResult(true, 0); // honeypot: pretend success, store nothing
        var lead = new Lead
        {
            Kind = i.Type == LeadKinds.Support ? LeadKinds.Support : LeadKinds.Project,
            Name = Line(i.Name, 120), Email = Text.Email(i.Email), Phone = Line(i.Phone, 40), Service = Line(i.Service, 160),
            Details = Clip(i.Details, 5000), Lang = Langs.Of(i.Lang), Page = Line(i.Page, 300), Ip = current.Ip, CreatedAt = Now,
        };
        if (lead.Name == "" || lead.Details == "" || lead.Email == "" && lead.Phone == "") throw new DomainException("أدخل الاسم والتفاصيل والبريد أو الجوال", "invalid");
        if (lead.Email != "" && !IsEmail(lead.Email)) throw new DomainException("البريد الإلكتروني غير صحيح", "email");
        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.TeamLead(lead, site.Value.TeamEmail));
        if (IsEmail(lead.Email)) email.Enqueue(Templates.LeadThanks(lead, site.Value.WhatsApp));
        return new SubmitResult(true, lead.Id);
    }

    public async Task<SubmitResult> Handle(PartnerSignupCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new SubmitResult(true, 0);
        var mail = Text.Email(i.Email);
        var name = Line(i.Name, 120);
        if (name == "" || !IsPhone(i.Phone)) throw new DomainException("أدخل الاسم ورقم الجوال", "invalid");
        if (!IsEmail(mail)) throw new DomainException("البريد الإلكتروني غير صحيح", "email");
        var p = await db.Partners.FirstOrDefaultAsync(x => x.Email == mail, ct);
        if (p == null)
        {
            p = new Partner { Email = mail, CreatedAt = Now, Code = await NewCodeAsync(ct) };
            db.Partners.Add(p);
        }
        p.Name = name;
        p.Company = Line(i.Company, 120);
        p.Phone = Line(i.Phone, 40);
        p.Lang = Langs.Of(i.Lang);
        p.UserId ??= await db.Users.Where(u => u.Email == mail && u.EmailVerified).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        await db.SaveChangesAsync(ct);
        var link = PartnerLink(site.Value, p.Code);
        var lead = new Lead { Kind = LeadKinds.Partner, Name = p.Name, Email = p.Email, Phone = p.Phone, Company = p.Company, Details = link, Lang = p.Lang, Ip = current.Ip, CreatedAt = Now };
        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.PartnerWelcome(p, link));
        email.Enqueue(Templates.TeamLead(lead, site.Value.TeamEmail));
        return new SubmitResult(true, p.Id);
    }

    public static string PartnerLink(SiteOptions s, string code) => $"{s.SiteUrl.TrimEnd('/')}/?ref={code}";

    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        for (var attempt = 0; ; attempt++)
        {
            var code = "ox" + new string(Enumerable.Range(0, 6).Select(_ => alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
            if (attempt > 5 || !await db.Partners.AnyAsync(p => p.Code == code, ct)) return code;
        }
    }

    public async Task<SubmitResult> Handle(TestimonialCommand cmd, CancellationToken ct)
    {
        var i = cmd.Input;
        if (!string.IsNullOrEmpty(i.CompanyWebsite)) return new SubmitResult(true, 0);
        var t = new Testimonial
        {
            Name = Line(i.Name, 80), Email = Text.Email(i.Email), Role = Line(i.Role, 80), Company = Line(i.Company, 100),
            Rating = Math.Clamp(i.Rating ?? 5, 1, 5), Text = Clip(i.Text, 800), Lang = Langs.Of(i.Lang), CreatedAt = Now,
        };
        if (t.Name == "") throw new DomainException("اكتب اسمك", "name");
        if (!IsEmail(t.Email)) throw new DomainException("البريد الإلكتروني غير صحيح", "email");
        if (t.Text.Length < 15) throw new DomainException("اكتب رأيك في 15 حرفًا على الأقل", "text");
        if ((i.Consent ?? "").Trim().ToLowerInvariant() is not ("yes" or "on" or "true" or "1")) throw new DomainException("وافق على نشر رأيك", "consent");
        db.Testimonials.Add(t);
        await db.SaveChangesAsync(ct);
        email.Enqueue(Templates.TeamTestimonial(t, site.Value.TeamEmail));
        return new SubmitResult(true, t.Id);
    }

    public async Task<ProofDto> Handle(ProofQuery q, CancellationToken ct)
    {
        var list = await db.Testimonials.AsNoTracking().Where(t => t.Status == "approved").OrderByDescending(t => t.Rating).ThenByDescending(t => t.CreatedAt).Take(12)
            .Select(t => new ProofTestimonial(t.Name, t.Role, t.Company, t.Rating, t.Text, t.Lang)).ToListAsync(ct);
        return new ProofDto(await sites.PublishedSitesAsync(ct), list);
    }

    public async Task<List<LeadDto>> Handle(ListLeadsQuery q, CancellationToken ct)
    {
        await access.StaffAsync(ct);
        var query = db.Leads.AsNoTracking();
        if (!string.IsNullOrEmpty(q.Kind)) query = query.Where(l => l.Kind == q.Kind);
        return await query.OrderByDescending(l => l.Id).Take(Math.Clamp(q.Take, 1, 500))
            .Select(l => new LeadDto(l.Id, l.Kind, l.Name, l.Email, l.Phone, l.Company, l.Service, l.Details, l.Lang, l.Page, l.Status, l.CreatedAt)).ToListAsync(ct);
    }

    private static TestimonialDto Dto(Testimonial t) => new(t.Id, t.Name, t.Email, t.Role, t.Company, t.Rating, t.Text, t.Lang, t.Status, t.CreatedAt);

    public async Task<List<TestimonialDto>> Handle(ListTestimonialsQuery q, CancellationToken ct)
    {
        await access.StaffAsync(ct);
        var query = db.Testimonials.AsNoTracking();
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(t => t.Status == q.Status);
        return (await query.OrderByDescending(t => t.Id).Take(200).ToListAsync(ct)).Select(Dto).ToList();
    }

    public async Task<TestimonialDto> Handle(ModerateTestimonialCommand cmd, CancellationToken ct)
    {
        await access.StaffAsync(ct);
        if (cmd.Status is not ("approved" or "rejected" or "pending")) throw new DomainException("حالة غير معروفة", "bad_request");
        var t = await db.Testimonials.FirstOrDefaultAsync(x => x.Id == cmd.Id, ct) ?? throw DomainException.NotFound();
        t.Status = cmd.Status;
        await db.SaveChangesAsync(ct);
        return Dto(t);
    }
}
