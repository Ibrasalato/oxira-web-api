using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Features.Status;

public record ServiceStatus(string Key, bool? Ok, long? Since, long? Checked, double? Uptime24h, int Checks, List<int> History);
public record StatusView(long Updated, int Interval, List<ServiceStatus> Services);

public record StatusQuery : IQuery<StatusView>;
/// <summary>scheduled (every 15 minutes): one probe per service</summary>
public record CheckServicesCommand : ICommand<int>;

/// <summary>The public status page: uptime checks of the Oxira services, kept for 30 days.</summary>
public class StatusHandlers(IAppDbContext db, TimeProvider clock, IUptimeProbe probe, IOptions<StatusOptions> opt)
    : IRequestHandler<StatusQuery, StatusView>, IRequestHandler<CheckServicesCommand, int>
{
    private const int Bars = 96;

    public async Task<int> Handle(CheckServicesCommand cmd, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var checks = await Task.WhenAll(opt.Value.Services.Where(s => !string.IsNullOrWhiteSpace(s.Value)).Select(async s =>
        {
            var (ok, ms) = await probe.ProbeAsync(s.Value, ct);
            return new StatusCheck { Key = s.Key, Ok = ok, Ms = ms, At = now };
        }));
        db.StatusChecks.AddRange(checks);
        await db.SaveChangesAsync(ct);
        var old = now.AddDays(-30);
        await db.StatusChecks.Where(c => c.At < old).ExecuteDeleteAsync(ct);
        return checks.Length;
    }

    public async Task<StatusView> Handle(StatusQuery q, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var day = now.AddHours(-24);
        var week = now.AddDays(-7);
        var rows = await db.StatusChecks.AsNoTracking().Where(c => c.At > week).OrderBy(c => c.At).ToListAsync(ct);
        var services = opt.Value.Services.Where(s => !string.IsNullOrWhiteSpace(s.Value)).Select(s => s.Key).Select(key =>
        {
            var mine = rows.Where(r => r.Key == key).ToList();
            var last = mine.LastOrDefault();
            if (last == null) return new ServiceStatus(key, null, null, null, null, 0, []);
            // since: when the current state (up or down) started
            var since = last.At;
            for (var i = mine.Count - 1; i >= 0 && mine[i].Ok == last.Ok; i--) since = mine[i].At;
            var recent = mine.Where(r => r.At > day).ToList();
            double? uptime = recent.Count == 0 ? null : Math.Round(100.0 * recent.Count(r => r.Ok) / recent.Count, 2);
            return new ServiceStatus(key, last.Ok, since.ToUnixTimeMilliseconds(), last.At.ToUnixTimeMilliseconds(), uptime, recent.Count,
                mine.TakeLast(Bars).Select(r => r.Ok ? 1 : 0).ToList());
        }).ToList();
        var updated = rows.Count == 0 ? now : rows[^1].At;
        return new StatusView(updated.ToUnixTimeMilliseconds(), opt.Value.Minutes, services);
    }
}
