using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Oxira.Web.Api.Infrastructure;
using Oxira.Web.Domain;

namespace Oxira.Web.Tests;

public class SecurityTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Pre_account_takeover_is_undone_by_the_real_owner()
    {
        var c = app.Client();
        // the attacker signs up with the victim's email and never verifies it
        await c.PostJson("/api/auth/signup", new { email = "victim@example.com", name = "Attacker", password = "Attack123" });
        // the victim signs in with a magic link
        await c.PostJson("/api/auth/magic-link", new { email = "victim@example.com", lang = "en", returnUrl = "https://design.oxira.sa/" });
        var code = Regex.Match(app.Email.To("victim@example.com").Last().Html, @"k=([a-f0-9]{40})").Groups[1].Value;
        var (s, auth) = await c.PostJson("/api/auth/magic-link/complete", new { code });
        Assert.Equal(HttpStatusCode.OK, s);
        var (_, me) = await app.Client(auth.Str("token")).GetJson("/api/auth/me");
        Assert.False(me.GetProperty("has_password").GetBoolean());
        var (_, login) = await c.PostJson("/api/auth/login", new { email = "victim@example.com", password = "Attack123" });
        Assert.Equal("invalid", login.Str("reason"));
        Assert.Null(await app.Db(db => db.Users.Where(u => u.Email == "victim@example.com").Select(u => u.PasswordHash).SingleAsync()));

        // requesting a magic link does not verify a new address by itself
        await c.PostJson("/api/auth/magic-link", new { email = "notyet@example.com", returnUrl = "https://oxira.sa/" });
        Assert.False(await app.Db(db => db.Users.Where(u => u.Email == "notyet@example.com").Select(u => u.EmailVerified).SingleAsync()));
    }

    [Fact]
    public async Task Admin_role_only_after_the_email_is_verified()
    {
        var c = app.Client();
        await c.PostJson("/api/auth/signup", new { email = "boss@oxira.sa", name = "Boss", password = "Boss12345" });
        Assert.Equal(Roles.Customer, await app.Db(db => db.Users.Where(u => u.Email == "boss@oxira.sa").Select(u => u.Role).SingleAsync()));
        var (_, auth) = await c.PostJson("/api/auth/verify", new { email = "boss@oxira.sa", code = app.CodeFor("boss@oxira.sa"), purpose = "verify" });
        Assert.Equal(Roles.Admin, auth.GetProperty("user").Str("role"));
        Assert.Equal(HttpStatusCode.OK, (await app.Client(auth.Str("token")).GetJson("/api/admin/users")).Status);
    }

    [Fact]
    public async Task Own_domains_need_dns_proof_and_cannot_be_taken_over()
    {
        var (token, orderId, userId) = await Seed.SiteAsync(app, "domain.owner@example.com", "dom-a", []);
        await app.Db(db => db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(x => x
            .SetProperty(o => o.DomainOption, "own").SetProperty(o => o.DomainName, "owned-shop.com").SetProperty(o => o.VerifyToken, "oxira-verify=abc123").SetProperty(o => o.Verified, false)));

        // the attacker cannot order a site on a domain another paid site holds
        var attacker = await app.TokenAsync("hijack@example.com");
        var (os, ob) = await app.Client(attacker).PostJson("/api/orders/site", new { name = "X", phone = "0500000000", addons = "plan:pro", domainOption = "own", domainName = "www.owned-shop.com", spec = StudioTests.Spec() });
        Assert.Equal(HttpStatusCode.Conflict, os);
        Assert.Equal("domain", ob.Str("reason"));
        var (es, eb) = await app.Client(attacker).PostJson("/api/sites/external", new { domain = "owned-shop.com" });
        Assert.Equal(HttpStatusCode.Conflict, es);
        Assert.Equal("domain", eb.Str("reason"));

        // publishing keeps the slug until the owner proves the domain
        var owner = app.Client(token);
        await owner.PostJson("/api/portal/menu", new { order_id = orderId, menu = new { title = "M", note = "", categories = Array.Empty<object>() } });
        Assert.True(app.Kv.Keys.ContainsKey("slug:dom-a"));
        Assert.False(app.Kv.Keys.ContainsKey("host:owned-shop.com"));
        var site = (await owner.GetJson("/api/portal")).Body.GetProperty("sites")[0];
        Assert.Equal("oxira-verify=abc123", site.Str("verify_txt"));
        Assert.False((await owner.PostJson($"/api/sites/external/{orderId}/check", new { })).Body.GetProperty("verified").GetBoolean());
        app.Dns.Txt["owned-shop.com"] = ["oxira-verify=abc123"];
        Assert.True((await owner.PostJson($"/api/sites/external/{orderId}/check", new { })).Body.GetProperty("verified").GetBoolean());
        Assert.True(app.Kv.Keys.ContainsKey("host:owned-shop.com"));
        Assert.Contains($"\"_order\":{orderId}", app.Kv.Keys["host:owned-shop.com"]);
    }

    private static string IpOf(string remote, string? cfHeader, bool trust = true, string[]? ranges = null)
    {
        var settings = new Dictionary<string, string?> { ["Proxy:TrustCloudflare"] = trust ? "true" : "false" };
        for (var i = 0; ranges != null && i < ranges.Length; i++) settings[$"Proxy:CloudflareRanges:{i}"] = ranges[i];
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (cfHeader != null) ctx.Request.Headers["CF-Connecting-IP"] = cfHeader;
        return new HttpCurrentUser(new HttpContextAccessor { HttpContext = ctx }, cfg).Ip;
    }

    [Fact]
    public void Cf_connecting_ip_is_trusted_only_from_cloudflare()
    {
        Assert.Equal("1.2.3.4", IpOf("1.2.3.4", "9.9.9.9"));                      // not Cloudflare: header ignored
        Assert.Equal("9.9.9.9", IpOf("173.245.48.10", "9.9.9.9"));                // Cloudflare edge
        Assert.Equal("9.9.9.9", IpOf("::ffff:162.158.1.2", "9.9.9.9"));           // IPv4-mapped IPv6
        Assert.Equal("2001:db8::1", IpOf("2606:4700::1", "2001:db8::1"));        // IPv6 edge
        Assert.Equal("173.245.48.10", IpOf("173.245.48.10", "not-an-ip"));       // junk header
        Assert.Equal("173.245.48.10", IpOf("173.245.48.10", "9.9.9.9", trust: false));
        Assert.Equal("9.9.9.9", IpOf("10.0.0.5", "9.9.9.9", ranges: ["10.0.0.0/8"])); // own proxy list
    }
}
