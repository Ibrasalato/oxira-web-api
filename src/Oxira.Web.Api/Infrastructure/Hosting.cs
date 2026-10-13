namespace Oxira.Web.Api.Infrastructure;

public static class Hosting
{
    /// <summary>scheduled jobs (BackgroundServices) and API-side adapters</summary>
    public static IServiceCollection AddApiHosting(this IServiceCollection s, IConfiguration cfg)
    {
        return s;
    }
}
