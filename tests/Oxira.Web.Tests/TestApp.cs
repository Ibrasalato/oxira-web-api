using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Oxira.Web.Application.Common;
using Oxira.Web.Infrastructure.Data;

namespace Oxira.Web.Tests;

/// <summary>The real API on a real SQL Server (OXIRA_TEST_DB), with fakes for email, AI, payments, publishing and outbound HTTP.</summary>
public partial class TestApp : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero)); // Tue 10:00 Riyadh
    public FakeEmail Email { get; } = new();
    private readonly string _db = "OxiraWebTest_" + Guid.NewGuid().ToString("N")[..8];

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        var server = Environment.GetEnvironmentVariable("OXIRA_TEST_DB") ?? "Server=localhost,1433;User Id=sa;Password=Oxira_dev_2026!;TrustServerCertificate=True";
        b.UseEnvironment("Development");
        b.UseSetting("ConnectionStrings:Web", $"{server};Database={_db}");
        foreach (var k in new[] { "AuthPerMinute", "RefreshPerMinute", "FormsPerMinute", "ChatPerMinute", "AiPerMinute", "UploadPerMinute" })
            b.UseSetting($"RateLimit:{k}", "100000");
        b.UseSetting("Auth:AdminEmails:0", "boss@oxira.sa");
        b.UseSetting("Site:ApiUrl", "https://api.test");
        b.UseSetting("Site:SiteUrl", "https://oxira.sa");
        Configure(b);
        b.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(Email);
            // scheduled jobs run only when a test calls them
            foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Namespace?.StartsWith("Oxira.Web.Api") == true).ToList()) s.Remove(d);
            Fakes(s);
        });
    }

    partial void Configure(IWebHostBuilder b);
    partial void Fakes(IServiceCollection s);

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OxiraDbContext>().Database.EnsureDeletedAsync();
        }
        catch { /* best effort */ }
        await base.DisposeAsync();
    }

    public HttpClient Client(string? token = null)
    {
        var c = CreateClient();
        if (token != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public async Task<T> Db<T>(Func<OxiraDbContext, Task<T>> f)
    {
        await using var scope = Services.CreateAsyncScope();
        return await f(scope.ServiceProvider.GetRequiredService<OxiraDbContext>());
    }

    public Task Db(Func<OxiraDbContext, Task> f) => Db<int>(async db => { await f(db); return 0; });

    /// <summary>the last 6-digit code emailed to this address (the code starts the subject)</summary>
    public string CodeFor(string email)
    {
        var m = Email.To(email).LastOrDefault(x => Regex.IsMatch(x.Subject, @"^\d{6} ")) ?? throw new InvalidOperationException($"no code emailed to {email}");
        return m.Subject[..6];
    }

    /// <summary>sign up, verify, and return the session (token, refreshToken …)</summary>
    public async Task<JsonElement> SignUpAsync(string email, string password = "Secret123", string name = "Test User", string? role = null)
    {
        var c = Client();
        var (s1, b1) = await c.PostJson("/api/auth/signup", new { email, name, password, phone = "0500000000", company = "Co", lang = "ar" });
        Assert.True(s1 == HttpStatusCode.OK, b1.ToString());
        var (s2, b2) = await c.PostJson("/api/auth/verify", new { email, code = CodeFor(email), purpose = "verify" });
        Assert.True(s2 == HttpStatusCode.OK, b2.ToString());
        if (role == null) return b2;
        await Db(db => db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, role)));
        var (_, b3) = await c.PostJson("/api/auth/login", new { email, password });
        return b3;
    }

    public async Task<string> TokenAsync(string email, string? role = null) => (await SignUpAsync(email, role: role)).GetProperty("token").GetString()!;
}

public static class Http
{
    public static async Task<(HttpStatusCode Status, JsonElement Body)> PostJson(this HttpClient c, string url, object body)
    {
        var res = await c.PostAsJsonAsync(url, body);
        return (res.StatusCode, await Read(res));
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> PutJson(this HttpClient c, string url, object body)
    {
        var res = await c.PutAsJsonAsync(url, body);
        return (res.StatusCode, await Read(res));
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> PostForm(this HttpClient c, string url, Dictionary<string, string> form)
    {
        var res = await c.PostAsync(url, new FormUrlEncodedContent(form));
        return (res.StatusCode, await Read(res));
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> GetJson(this HttpClient c, string url)
    {
        var res = await c.GetAsync(url);
        return (res.StatusCode, await Read(res));
    }

    public static async Task<JsonElement> Read(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        if ((int)res.StatusCode >= 500) throw new HttpRequestException($"{(int)res.StatusCode} {res.RequestMessage?.RequestUri}: {text}");
        return text.Length > 0 && (text[0] == '{' || text[0] == '[') ? JsonDocument.Parse(text).RootElement.Clone() : default;
    }

    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString() ?? "";
}

public class FakeEmail : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();
    public void Enqueue(EmailMessage message) => Sent.Enqueue(message);
    public List<EmailMessage> To(string address) => Sent.Where(m => m.To.Contains(address)).ToList();
}
