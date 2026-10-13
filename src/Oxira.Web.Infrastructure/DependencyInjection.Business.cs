using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oxira.Web.Application.Common;
using Oxira.Web.Infrastructure.Data;
using Oxira.Web.Infrastructure.Services;

namespace Oxira.Web.Infrastructure;

public static partial class DependencyInjection
{
    static partial void AddBusiness(IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<AnthropicOptions>(cfg.GetSection("Anthropic"));
        s.AddHttpClient<IAnthropic, AnthropicClient>(h => h.Timeout = TimeSpan.FromSeconds(120));

        // visitors' websites: public addresses only (see PublicNetwork)
        s.AddHttpClient(PublicNetwork.ClientName, h => h.Timeout = TimeSpan.FromSeconds(20)).ConfigurePrimaryHttpMessageHandler(PublicNetwork.Handler);
        s.AddSingleton<ISafeFetcher, SafeFetcher>();

        s.AddScoped<IDbSeed, ExampleDemosSeed>();
        s.AddScoped<Oxira.Web.Application.Features.Public.ISiteCounter, OrderSiteCounter>();
        AddCommerce(s, cfg);
    }

    static partial void AddCommerce(IServiceCollection s, IConfiguration cfg);
}
