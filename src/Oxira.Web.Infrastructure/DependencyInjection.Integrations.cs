using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oxira.Web.Application.Common;
using Oxira.Web.Infrastructure.Services;

namespace Oxira.Web.Infrastructure;

public static partial class DependencyInjection
{
    static partial void AddIntegrations(IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<ISecretProtector, DataProtectionSecrets>();
        s.AddSingleton<IWordPressClient, WordPressClient>();
        s.Configure<MetaOptions>(cfg.GetSection("Meta"));
        s.AddHttpClient<IMetaClient, MetaClient>(h => h.Timeout = TimeSpan.FromSeconds(60));
        s.AddHttpClient<IUptimeProbe, HttpUptimeProbe>(h => h.Timeout = TimeSpan.FromSeconds(15));
    }
}
