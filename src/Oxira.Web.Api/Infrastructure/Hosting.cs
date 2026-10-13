using MediatR;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Application.Features.Seo;
using Oxira.Web.Application.Features.Social;
using Oxira.Web.Application.Features.Status;

namespace Oxira.Web.Api.Infrastructure;

public static class Hosting
{
    /// <summary>scheduled jobs (BackgroundServices)</summary>
    public static IServiceCollection AddApiHosting(this IServiceCollection s, IConfiguration cfg)
    {
        if (cfg.GetValue("Jobs:Enabled", true)) s.AddHostedService<JobsHost>();
        return s;
    }
}

/// <summary>
/// The scheduler (replaces n8n's cron workflows). Every job is idempotent, so a restart or a second server never repeats work:
/// status checks every 15 minutes, renewals hourly, due social posts every 5 minutes, AI drafts (SEO, social plans, content) every 6 hours.
/// </summary>
public class JobsHost(IServiceScopeFactory scopes, ILogger<JobsHost> log, IConfiguration cfg) : BackgroundService
{
    private readonly Dictionary<string, DateTimeOffset> _last = [];

    private (string Name, TimeSpan Every, Func<ISender, CancellationToken, Task<int>> Run)[] Jobs =>
    [
        ("status", TimeSpan.FromMinutes(Math.Max(1, cfg.GetValue("Status:Minutes", 15))), (m, ct) => m.Send(new CheckServicesCommand(), ct)),
        ("renewals", TimeSpan.FromHours(1), (m, ct) => m.Send(new RunRenewalsCommand(), ct)),
        ("publish", TimeSpan.FromMinutes(5), (m, ct) => m.Send(new PublishDueSocialCommand(), ct)),
        ("seo", TimeSpan.FromHours(6), (m, ct) => m.Send(new DraftSeoCommand(), ct)),
        ("social", TimeSpan.FromHours(6), (m, ct) => m.Send(new DraftSocialCommand(), ct)),
        ("content", TimeSpan.FromHours(6), (m, ct) => m.Send(new DraftContentCommand(), ct)),
    ];

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stop); // let the database migrate first
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            foreach (var (name, every, run) in Jobs)
            {
                if (_last.TryGetValue(name, out var at) && DateTimeOffset.UtcNow - at < every) continue;
                _last[name] = DateTimeOffset.UtcNow;
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var n = await run(scope.ServiceProvider.GetRequiredService<ISender>(), stop);
                    if (n > 0) log.LogInformation("Job {Job}: {Count}", name, n);
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Job {Job} failed", name); }
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
