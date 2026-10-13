using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Infrastructure.Services;

namespace Oxira.Web.Tests;

public class PublicTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Contact_form_as_urlsearchparams()
    {
        var (s, b) = await app.Client().PostForm("/api/contact", new()
        {
            ["type"] = "support", ["name"] = "Omar", ["email"] = "omar@example.com", ["phone"] = "+966500000009", ["service"] = "Classti",
            ["details"] = "الموقع لا يفتح", ["lang"] = "ar", ["page"] = "https://oxira.sa/contact/",
        });
        Assert.Equal(HttpStatusCode.OK, s);
        Assert.True(b.GetProperty("success").GetBoolean());
        var id = b.GetProperty("id").GetInt32();
        var team = app.Email.Sent.Single(m => m.To.Contains("info@oxira.sa") && m.Subject.Contains($"رقم {id}"));
        Assert.Equal("omar@example.com", team.ReplyTo);
        Assert.Contains("دعم فني", team.Subject);
        Assert.Contains(app.Email.To("omar@example.com"), m => m.Html.Contains($"<b>{id}</b>"));

        var (bad, bb) = await app.Client().PostForm("/api/contact", new() { ["name"] = "x", ["details"] = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Equal("invalid", bb.Str("reason"));
        Assert.Equal("invalid", bb.Str("error"));
        var (_, trap) = await app.Client().PostForm("/api/contact", new() { ["name"] = "bot", ["details"] = "spam", ["phone"] = "123456789", ["company_website"] = "x" });
        Assert.Equal(0, trap.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Partner_signup_and_reviews_with_moderation()
    {
        var (_, p) = await app.Client().PostForm("/api/partners", new() { ["name"] = "Fahad", ["company"] = "Agency", ["phone"] = "0551112222", ["email"] = "fahad@example.com", ["lang"] = "ar" });
        Assert.True(p.GetProperty("success").GetBoolean());
        Assert.Matches(@"https://oxira\.sa/\?ref=ox[a-z0-9]{6}", app.Email.To("fahad@example.com").Single().Html);

        var (bad, bb) = await app.Client().PostForm("/api/testimonials", new() { ["name"] = "Mona", ["email"] = "mona@example.com", ["rating"] = "5", ["text"] = "short", ["consent"] = "yes" });
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Equal("text", bb.Str("reason"));
        var (_, t) = await app.Client().PostForm("/api/testimonials", new()
        {
            ["name"] = "Mona", ["email"] = "mona@example.com", ["role"] = "CEO", ["company"] = "Bloom", ["rating"] = "4",
            ["text"] = "خدمة ممتازة وسريعة، الموقع جاهز في يوم واحد", ["consent"] = "yes", ["lang"] = "ar",
        });
        var id = t.GetProperty("id").GetInt32();
        var (_, proof) = await app.Client().GetJson("/api/proof");
        Assert.Empty(proof.GetProperty("testimonials").EnumerateArray());
        var staff = await app.TokenAsync("staff1@oxira.sa", "staff");
        var (ms, _) = await app.Client(staff).PutJson($"/api/admin/testimonials/{id}", new { status = "approved" });
        Assert.Equal(HttpStatusCode.OK, ms);
        var (_, proof2) = await app.Client().GetJson("/api/proof");
        var item = proof2.GetProperty("testimonials")[0];
        Assert.Equal("Mona", item.Str("name"));
        Assert.Equal(4, item.GetProperty("rating").GetInt32());
        Assert.True(proof2.TryGetProperty("sites", out _));
    }

    [Fact]
    public async Task Chat_saves_a_lead_with_the_tool_and_accepts_text_plain()
    {
        app.Ai.Script.Enqueue(FakeAnthropicExt.Tool("save_lead", new JsonObject { ["name"] = "Huda", ["phone"] = "966511111111", ["service"] = "website", ["details"] = "متجر عطور" }));
        app.Ai.Script.Enqueue(FakeAnthropicExt.Text("تم تسجيل طلبك ✅"));
        var body = JsonSerializer.Serialize(new { sessionId = "sess-abc-123", message = "ابي متجر", lang = "ar", page = "https://oxira.sa/" });
        var res = await app.Client().PostAsync("/api/chat", new StringContent(body, Encoding.UTF8, "text/plain"));
        var r = await Http.Read(res);
        Assert.True(r.GetProperty("success").GetBoolean(), r.ToString());
        Assert.Equal("تم تسجيل طلبك ✅", r.Str("reply"));
        Assert.Contains(app.Email.Sent, m => m.To.Contains("info@oxira.sa") && m.Subject.Contains("Huda"));
        var lead = await app.Db(db => db.Leads.SingleAsync(l => l.Kind == "chat"));
        Assert.Equal("متجر عطور", lead.Details);

        // memory: the next call carries the earlier turns
        await app.Client().PostForm("/api/chat", new() { ["sessionId"] = "sess-abc-123", ["message"] = "شكرا", ["lang"] = "ar" });
        var last = app.Ai.Calls[^1];
        Assert.Equal(3, last.Messages.Count);
        var (_, hist) = await app.Client().GetJson("/api/chat/sess-abc-123");
        Assert.Equal(4, hist.GetArrayLength());
    }

    [Fact]
    public async Task Audit_scores_the_page()
    {
        app.Web.Page("https://shop.example.com/", """
            <!doctype html><html lang="ar"><head><title>متجر المثال للعطور الفاخرة</title>
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="description" content="أفضل العطور الشرقية والفرنسية في الرياض مع توصيل سريع لكل مدن المملكة.">
            <meta property="og:title" content="متجر المثال"><link rel="canonical" href="https://shop.example.com/">
            </head><body><h1>عطور</h1><img src="a.jpg" alt="عطر"><img src="b.jpg"></body></html>
            """);
        app.Web.Page("https://shop.example.com/robots.txt", "User-agent: *\nSitemap: https://shop.example.com/sm.xml");
        app.Web.Page("https://shop.example.com/sm.xml", "<urlset><url><loc>https://shop.example.com/</loc></url></urlset>");
        var (s, r) = await app.Client().PostForm("/api/audit", new() { ["url"] = "shop.example.com", ["name"] = "Ali", ["email"] = "ali@example.com", ["lang"] = "ar" });
        Assert.Equal(HttpStatusCode.OK, s);
        Assert.Equal("shop.example.com", r.Str("host"));
        var checks = r.GetProperty("checks").EnumerateArray().ToDictionary(c => c.Str("id"), c => c.GetProperty("ok").GetBoolean());
        Assert.Equal(14, checks.Count);
        Assert.True(checks["title"] && checks["description"] && checks["viewport"] && checks["sitemap"] && checks["robots"] && checks["canonical"] && checks["og"]);
        Assert.False(checks["alt"]);
        Assert.False(checks["schema"]);
        var score = r.GetProperty("score").GetInt32();
        Assert.InRange(score, 50, 99);
        foreach (var k in new[] { "performance", "seo", "accessibility", "bestPractices" }) Assert.InRange(r.GetProperty("scores").GetProperty(k).GetInt32(), 0, 100);
        Assert.Contains(app.Email.To("ali@example.com"), m => m.Subject.Contains($"{score}/100"));

        var (bad, bb) = await app.Client().PostForm("/api/audit", new() { ["url"] = "not a url", ["name"] = "Ali", ["email"] = "ali@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Equal("url", bb.Str("error"));
        var (_, mail) = await app.Client().PostForm("/api/audit", new() { ["url"] = "example.org", ["name"] = "Ali", ["email"] = "nope" });
        Assert.Equal("email", mail.Str("error"));
    }

    [Fact]
    public async Task Whatsapp_agent_demo_try_chat_and_subscribe()
    {
        app.Web.Page("https://cafe.example.com/", "<html><head><title>Bean House</title></head><body>" + string.Concat(Enumerable.Repeat("<p>Specialty coffee in Riyadh. Latte 18 SAR, V60 22 SAR, open 7am-1am, delivery via app.</p>", 6)) + "</body></html>");
        app.Ai.Script.Enqueue(FakeAnthropicExt.Text("""
            Here you go: {"business":"Bean House","system_prompt":"You are the WhatsApp assistant of Bean House, a specialty coffee shop in Riyadh. Latte 18 SAR, V60 22 SAR. Reply in Saudi Arabic.","welcome":"هلا! أنا مساعد Bean House ☕","suggestions":["كم سعر اللاتيه؟","متى تفتحون؟","توصلون؟"]}
            """));
        var (s, r) = await app.Client().PostJson("/api/agent/demos", new { url = "https://cafe.example.com", business = "بين هاوس", notes = "", email = "owner@cafe.example.com", phone = "", lang = "ar" });
        Assert.Equal(HttpStatusCode.OK, s);
        var demoId = r.Str("demoId");
        Assert.Matches("^[a-f0-9]{12}$", demoId);
        Assert.Equal("Bean House", r.Str("business"));
        Assert.Contains(app.Email.To("owner@cafe.example.com"), m => m.Html.Contains($"?d={demoId}"));

        var (_, d) = await app.Client().GetJson($"/api/agent/demo?d={demoId}");
        Assert.False(d.GetProperty("example").GetBoolean());
        Assert.Equal(3, d.GetProperty("suggestions").GetArrayLength());

        app.Ai.Script.Enqueue(FakeAnthropicExt.Text("اللاتيه *18 ريال* ☕"));
        var (cs, c) = await app.Client().PostJson("/api/agent/chat", new { demoId, message = "كم اللاتيه؟", history = new[] { new { role = "assistant", content = "هلا!" } } });
        Assert.Equal(HttpStatusCode.OK, cs);
        Assert.Equal(29, c.GetProperty("left").GetInt32());
        Assert.Contains("Bean House", app.Ai.Calls[^1].System);
        Assert.Equal("user", (string?)app.Ai.Calls[^1].Messages[0]!["role"]);

        await app.Db(db => db.AgentDemos.Where(x => x.Id == demoId).ExecuteUpdateAsync(x => x.SetProperty(a => a.Messages, 30)));
        var (ls, l) = await app.Client().PostJson("/api/agent/chat", new { demoId, message = "وبعد؟" });
        Assert.Equal(HttpStatusCode.TooManyRequests, ls);
        Assert.Equal("limit", l.Str("error"));
        var (ns, n) = await app.Client().PostJson("/api/agent/chat", new { demoId = "ffffffffffff", message = "hi" });
        Assert.Equal(HttpStatusCode.NotFound, ns);
        Assert.Equal("notfound", n.Str("error"));

        // the website's example businesses exist
        var (es, ex) = await app.Client().GetJson("/api/agent/demos/897fa1eda859");
        Assert.Equal(HttpStatusCode.OK, es);
        Assert.True(ex.GetProperty("example").GetBoolean());

        var (_, input) = await app.Client().PostJson("/api/agent/demos", new { url = "", business = "X", notes = "", email = "a@example.com" });
        Assert.Equal("input", input.Str("error"));
        var (_, fetch) = await app.Client().PostJson("/api/agent/demos", new { url = "https://down.example.com", business = "Down", notes = "", email = "a@example.com" });
        Assert.Equal("fetch", fetch.Str("error"));

        var (ss, sub) = await app.Client().PostJson("/api/agent/subscriptions", new { demoId, plan = "basic", name = "Majed", business = "Bean House", email = "owner@cafe.example.com", phone = "0500000001", whatsapp = "0500000002", lang = "ar" });
        Assert.Equal(HttpStatusCode.OK, ss);
        Assert.True(sub.GetProperty("id").GetInt32() > 0);
        Assert.Contains(app.Email.Sent, m => m.To.Contains("info@oxira.sa") && m.Subject.Contains("basic"));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Only_public_addresses_are_fetched(string ip, bool allowed) =>
        Assert.Equal(allowed, PublicNetwork.IsPublic(System.Net.IPAddress.Parse(ip)));

    [Fact]
    public void Only_web_urls_on_default_ports()
    {
        Assert.True(PublicNetwork.IsAllowedUrl(new Uri("https://example.com/a")));
        Assert.False(PublicNetwork.IsAllowedUrl(new Uri("https://example.com:8443/")));
        Assert.False(PublicNetwork.IsAllowedUrl(new Uri("ftp://example.com/")));
        Assert.False(PublicNetwork.IsAllowedUrl(new Uri("http://user:pw@example.com/")));
        Assert.False(PublicNetwork.IsAllowedUrl(new Uri("http://localhost/")));
    }
}

public static class FakeAnthropicExt
{
    public static Oxira.Web.Application.Common.AiResponse Text(string s) => FakeAnthropic.Text(s);
    public static Oxira.Web.Application.Common.AiResponse Tool(string name, JsonObject input) => FakeAnthropic.Tool(name, input);
}
