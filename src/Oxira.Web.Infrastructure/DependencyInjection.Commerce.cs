using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oxira.Web.Application.Common;
using Oxira.Web.Infrastructure.Services;

namespace Oxira.Web.Infrastructure;

public static partial class DependencyInjection
{
    static partial void AddCommerce(IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<CloudflareOptions>(cfg.GetSection("Cloudflare"));
        s.AddHttpClient<ISitePublisher, CloudflareKvPublisher>(h => h.Timeout = TimeSpan.FromSeconds(30));
        if (cfg.GetValue("Payments:Simulate", false))
        {
            s.AddSingleton<SimulatedGateway>();
            s.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<SimulatedGateway>());
            s.AddSingleton<IPaymentSimulator>(sp => sp.GetRequiredService<SimulatedGateway>());
        }
        else s.AddHttpClient<IPaymentGateway, MoyasarGateway>(h => h.Timeout = TimeSpan.FromSeconds(30));
        s.AddHttpClient<IDnsTxt, DohTxt>(h => h.Timeout = TimeSpan.FromSeconds(10));
        AddIntegrations(s, cfg);
    }

    static partial void AddIntegrations(IServiceCollection s, IConfiguration cfg);
}
