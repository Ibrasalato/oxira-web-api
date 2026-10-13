using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Auth;

namespace Oxira.Web.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<SiteOptions>(cfg.GetSection("Site"));
        s.Configure<AuthOptions>(cfg.GetSection("Auth"));
        s.Configure<AiOptions>(cfg.GetSection("Ai"));
        s.Configure<AgentOptions>(cfg.GetSection("Agent"));
        s.Configure<AuditOptions>(cfg.GetSection("Audit"));
        s.Configure<PricingOptions>(cfg.GetSection("Prices"));
        s.Configure<PaymentOptions>(cfg.GetSection("Payments"));
        s.Configure<SitesOptions>(cfg.GetSection("Sites"));
        s.Configure<Features.External.ExternalOptions>(cfg.GetSection("External"));
        s.Configure<StatusOptions>(cfg.GetSection("Status"));
        s.AddScoped<Access>();
        s.AddScoped<Sessions>();
        s.AddScoped<EmailCodes>();
        s.AddScoped<Features.Orders.PaymentService>();
        s.AddScoped<Features.Orders.SitePublishing>();
        s.AddScoped<Notifier>();
        s.AddMediatR(c =>
        {
            c.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            c.AddOpenBehavior(typeof(LoggingBehavior<,>));
        });
        return s;
    }
}
