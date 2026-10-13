using System.Net;

namespace Oxira.Web.Tests;

public class PlatformTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Swagger_openapi_health_and_cors()
    {
        var c = app.Client();
        var doc = await c.GetAsync("/openapi/v1.json");
        var text = await doc.Content.ReadAsStringAsync();
        Assert.True(doc.StatusCode == HttpStatusCode.OK, text[..Math.Min(text.Length, 2000)]);
        Assert.Contains("/api/auth/magic-link", text);
        Assert.Contains("Bearer", text);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);

        foreach (var origin in new[] { "https://oxira.sa", "https://www.oxira.sa", "https://design.oxira.sa", "https://events.oxira.sa", "https://ibrasalato.github.io", "http://localhost:4321" })
        {
            var pre = new HttpRequestMessage(HttpMethod.Options, "/api/portal");
            pre.Headers.Add("Origin", origin);
            pre.Headers.Add("Access-Control-Request-Method", "GET");
            pre.Headers.Add("Access-Control-Request-Headers", "authorization");
            var res = await c.SendAsync(pre);
            Assert.Equal(origin, res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }
        var evil = new HttpRequestMessage(HttpMethod.Options, "/api/portal");
        evil.Headers.Add("Origin", "https://evil.example");
        evil.Headers.Add("Access-Control-Request-Method", "GET");
        Assert.False((await c.SendAsync(evil)).Headers.Contains("Access-Control-Allow-Origin"));
    }
}
