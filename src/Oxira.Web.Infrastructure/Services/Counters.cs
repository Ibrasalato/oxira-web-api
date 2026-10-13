using Microsoft.Extensions.Configuration;
using Oxira.Web.Application.Features.Public;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>Published client sites for the proof strip: Proof:BaseSites (sites delivered before this API) plus published orders.</summary>
public class SiteCounter(IConfiguration cfg) : ISiteCounter
{
    public Task<int> PublishedSitesAsync(CancellationToken ct) => Task.FromResult(cfg.GetValue("Proof:BaseSites", 0));
}
