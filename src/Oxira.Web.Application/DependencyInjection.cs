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
        s.AddScoped<Access>();
        s.AddScoped<Sessions>();
        s.AddScoped<EmailCodes>();
        s.AddMediatR(c =>
        {
            c.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            c.AddOpenBehavior(typeof(LoggingBehavior<,>));
        });
        return s;
    }
}
