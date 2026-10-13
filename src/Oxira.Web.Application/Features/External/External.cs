using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Orders;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.External;

public record AddExternalInput(string? Domain, string? Name, string? Lang);
public record CheckExternalInput([property: JsonPropertyName("order_id")] int? OrderId);
public record AddExternalResult(bool Success, [property: JsonPropertyName("order_id")] int OrderId, string Txt);
public record CheckExternalResult(bool Success, bool Verified, string Txt, string Email, string Domain);

public record AddExternalSiteCommand(AddExternalInput Input) : ICommand<AddExternalResult>;
public record CheckExternalSiteCommand(int OrderId) : ICommand<CheckExternalResult>;

/// <summary>"External" section.</summary>
public class ExternalOptions
{
    /// <summary>the Google account clients add to Search Console (the other way to prove ownership)</summary>
    public string SearchConsoleEmail { get; set; } = "ibrasalato@gmail.com";
}

/// <summary>Websites hosted elsewhere, added for the SEO and social add-ons. Ownership: a DNS TXT record oxira-verify=… on the domain.</summary>
public class ExternalSiteHandlers(IAppDbContext db, TimeProvider clock, Access access, IDnsTxt dns, IOptions<ExternalOptions> opt, ILogger<ExternalSiteHandlers> log)
    : IRequestHandler<AddExternalSiteCommand, AddExternalResult>, IRequestHandler<CheckExternalSiteCommand, CheckExternalResult>
{
    public async Task<AddExternalResult> Handle(AddExternalSiteCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var domain = Domains.Normalize(cmd.Input.Domain);
        if (domain == "") throw new DomainException("اكتب اسم الدومين مثل example.com", "domain");
        var existing = await db.Orders.FirstOrDefaultAsync(o => o.UserId == a.Owner.Id && o.Kind == OrderKinds.External && o.DomainName == domain && o.Status != OrderStatuses.Cancelled, ct);
        if (existing != null) return new AddExternalResult(true, existing.Id, existing.VerifyToken);
        if (await db.Orders.CountAsync(o => o.UserId == a.Owner.Id && o.Kind == OrderKinds.External, ct) >= 30) throw DomainException.Limit("الحد الأقصى 30 موقعًا");
        var now = clock.GetUtcNow();
        var o = new Order
        {
            Kind = OrderKinds.External, UserId = a.Owner.Id, Email = a.Owner.Email, Name = a.Owner.Name, SiteName = Line(cmd.Input.Name, 120) is { Length: > 0 } n ? n : domain,
            DomainOption = "own", DomainName = domain, Url = $"https://{domain}/", Status = OrderStatuses.External, VerifyToken = $"oxira-verify={Hex(12)}",
            Lang = Langs.Of(cmd.Input.Lang ?? a.Owner.Lang), CreatedAt = now, UpdatedAt = now,
        };
        db.Orders.Add(o);
        await db.SaveChangesAsync(ct);
        return new AddExternalResult(true, o.Id, o.VerifyToken);
    }

    public async Task<CheckExternalResult> Handle(CheckExternalSiteCommand cmd, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var o = await PortalHandlers.OrderOfAsync(db, a, cmd.OrderId, ct);
        if (o.Kind != OrderKinds.External) throw DomainException.NotFound();
        if (!o.Verified)
        {
            IReadOnlyList<string> records = [];
            try { records = await dns.LookupAsync(o.DomainName, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "TXT lookup for {Domain} failed", o.DomainName); }
            if (records.Any(r => r.Trim().Trim('"') == o.VerifyToken))
            {
                o.Verified = true;
                o.UpdatedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
        }
        return new CheckExternalResult(true, o.Verified, o.VerifyToken, opt.Value.SearchConsoleEmail, o.DomainName);
    }
}
