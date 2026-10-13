using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oxira.Web.Application.Common;
using Oxira.Web.Infrastructure.Data;
using Oxira.Web.Infrastructure.Services;

namespace Oxira.Web.Infrastructure;

public static partial class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg)
    {
        var cs = cfg.GetConnectionString("Web") ?? throw new InvalidOperationException("ConnectionStrings:Web is missing");
        // no retrying execution strategy: handlers open their own transactions (BeginAsync)
        s.AddDbContext<OxiraDbContext>(o => o.UseSqlServer(cs, sql => sql.CommandTimeout(30)));
        s.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<OxiraDbContext>());
        s.AddScoped<DbInitializer>();
        s.AddScoped<IQuota, SqlQuota>();
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        s.AddDataProtection().SetApplicationName("oxira-web-api").PersistKeysToDbContext<OxiraDbContext>();

        s.Configure<SmtpOptions>(cfg.GetSection("Smtp"));
        s.AddSingleton<EmailQueue>();
        s.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<EmailQueue>());
        s.AddHostedService<EmailWorker>();
        AddBusiness(s, cfg);
        return s;
    }

    static partial void AddBusiness(IServiceCollection s, IConfiguration cfg);
}
