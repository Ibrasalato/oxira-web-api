using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Application.Features.Orders;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Features.Portal;

/// <summary>scheduled (hourly, idempotent): renewal invoices, past-due and cancellations</summary>
public record RunRenewalsCommand : ICommand<int>;

/// <summary>
/// Monthly add-ons and yearly hosting/domain renew with a Moyasar invoice issued 3 days before the due date.
/// Unpaid on the due date → past_due (still working 7 days); then cancelled. Cancelling subscriptions end on their due date.
/// Every change updates the site's features in the sites store (a cancelled hosting suspends the site).
/// </summary>
public class RenewalHandler(IAppDbContext db, TimeProvider clock, PaymentService payments, SitePublishing publishing, Notifier notifier, IEmailSender email,
    IOptions<PricingOptions> prices, IOptions<SiteOptions> site, ILogger<RenewalHandler> log) : IRequestHandler<RunRenewalsCommand, int>
{
    public const int NoticeDays = 3;
    public const int GraceDays = 7;

    public async Task<int> Handle(RunRenewalsCommand cmd, CancellationToken ct)
    {
        var today = Riyadh.Today(clock);
        var now = clock.GetUtcNow();
        var changed = new HashSet<int>();
        var done = 0;

        // 1. cancelling → cancelled on the due date
        foreach (var s in await db.Subscriptions.Where(s => s.Status == SubStatuses.Cancelling && s.NextDue != null && s.NextDue <= today).ToListAsync(ct))
        {
            s.Status = SubStatuses.Cancelled;
            s.CancelledAt ??= now;
            changed.Add(s.OrderId);
            done++;
        }
        await db.SaveChangesAsync(ct);

        // 2. renewal invoices a few days ahead
        var soon = today.AddDays(NoticeDays);
        var cutoff = today.AddDays(-GraceDays);
        foreach (var s in await db.Subscriptions.Where(s => (s.Status == SubStatuses.Active || s.Status == SubStatuses.PastDue) && s.Price > 0 && s.NextDue != null && s.NextDue <= soon && s.NextDue >= cutoff && s.PendingPaymentId == null).ToListAsync(ct))
        {
            try
            {
                var o = await db.Orders.FirstAsync(x => x.Id == s.OrderId, ct);
                var owner = o.UserId is { } uid ? await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct) : null;
                if (owner == null) continue;
                var url = await Subscriptions.StartPaymentAsync(db, payments, site.Value, prices.Value, s, o, owner, owner.Lang, now, ct);
                var siteName = o.SiteName != "" ? o.SiteName : o.DomainName;
                await notifier.NotifyAsync(owner.Id, NotificationTypes.Renew, product: s.Product, site: siteName, date: s.NextDue!.Value.ToString("yyyy-MM-dd"), orderId: o.Id, ct: ct);
                email.Enqueue(Templates.RenewalDue(owner.Email, s, siteName, url, prices.Value.Vat(s.Price) + s.Price, owner.Lang));
                done++;
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Renewal invoice for subscription {Sub} failed", s.Id); }
        }

        // 3. unpaid: past due on the date, cancelled after the grace period
        foreach (var s in await db.Subscriptions.Where(s => s.Status == SubStatuses.Active && s.NextDue != null && s.NextDue < today).ToListAsync(ct))
        {
            s.Status = SubStatuses.PastDue;
            changed.Add(s.OrderId);
            done++;
        }
        foreach (var s in await db.Subscriptions.Where(s => s.Status == SubStatuses.PastDue && s.NextDue != null && s.NextDue < cutoff).ToListAsync(ct))
        {
            s.Status = SubStatuses.Cancelled;
            s.CancelledAt = now;
            s.InvoiceUrl = "";
            changed.Add(s.OrderId);
            done++;
        }
        await db.SaveChangesAsync(ct);

        foreach (var id in changed)
        {
            try { await publishing.PushFeaturesAsync(await db.Orders.FirstAsync(o => o.Id == id, ct), ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Features of order {Order} not updated", id); }
        }
        return done;
    }
}
