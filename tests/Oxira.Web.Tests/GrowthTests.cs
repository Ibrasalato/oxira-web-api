using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Tests;

public static class Seed
{
    /// <summary>a client with a paid site and active add-ons</summary>
    public static async Task<(string Token, int OrderId, Guid UserId)> SiteAsync(TestApp app, string email, string slug, string[] addons, string kind = OrderKinds.Site, string settings = "{}")
    {
        var auth = await app.SignUpAsync(email);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var orderId = await app.Db(async db =>
        {
            var now = app.Clock.GetUtcNow();
            var o = new Order
            {
                Kind = kind, UserId = userId, Email = email, Name = "Owner", SiteName = "Rose " + slug, Slug = kind == OrderKinds.External ? "" : slug,
                Url = kind == OrderKinds.External ? $"https://{slug}.example.com/" : $"https://{slug}.oxira.sa/", DomainName = kind == OrderKinds.External ? $"{slug}.example.com" : "",
                Status = kind == OrderKinds.External ? OrderStatuses.External : OrderStatuses.Published, PaidAt = kind == OrderKinds.External ? null : now, Verified = true,
                SpecJson = StudioTests.Spec("Rose " + slug), SettingsJson = settings, CreatedAt = now, UpdatedAt = now,
            };
            db.Orders.Add(o);
            await db.SaveChangesAsync();
            foreach (var a in addons)
                db.Subscriptions.Add(new Subscription { OrderId = o.Id, UserId = userId, Product = a, Price = 99, Status = SubStatuses.Active, NextDue = DateOnly.FromDateTime(now.UtcDateTime).AddDays(20), CreatedAt = now });
            await db.SaveChangesAsync();
            return o.Id;
        });
        return (auth.Str("token"), orderId, userId);
    }
}

public class GrowthTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Seo_drafts_publish_to_the_site_and_to_wordpress()
    {
        var (token, orderId, _) = await Seed.SiteAsync(app, "seo@example.com", "seo-a", ["seo"]);
        var staff = await app.TokenAsync("seo.staff@oxira.sa", "staff");
        app.Ai.Script.Enqueue(FakeAnthropic.Text(JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new { kind = "meta", lang = "ar", title = "ورد الرياض | تنسيق زهور", description = "تنسيقات زهور طبيعية مع توصيل في نفس اليوم داخل الرياض.", body = "" },
                new { kind = "article", lang = "ar", title = "كيف تختار باقة الورد", description = "دليل سريع", body = "## مقدمة\nنص **مهم**\n- نقطة" },
                new { kind = "local", lang = "ar", title = "Flowers in Malqa", description = "Same-day delivery", body = "Local page" },
                new { kind = "spam", lang = "ar", title = "x", description = "", body = "" },
            },
        })));
        var (js, job) = await app.Client(staff).PostJson("/api/admin/jobs/seo", new { });
        Assert.Equal(HttpStatusCode.OK, js);
        Assert.Equal(3, job.GetProperty("count").GetInt32());
        // the second run within a week writes nothing
        Assert.Equal(0, (await app.Client(staff).PostJson("/api/admin/jobs/seo", new { })).Body.GetProperty("count").GetInt32());

        var c = app.Client(token);
        var (_, view) = await c.PostForm("/api/seo/list", new() { ["order_id"] = orderId.ToString() });
        var items = view.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.False(view.GetProperty("external").GetBoolean());
        var bell = (await c.GetJson("/api/account/notifications")).Body.GetProperty("items").EnumerateArray().First(x => x.Str("type") == "seo");
        Assert.Equal(3, bell.GetProperty("n").GetInt32());

        var article = items.First(i => i.Str("kind") == "article").GetProperty("id").GetInt32();
        var (ps, pub) = await c.PostForm("/api/seo/publish", new() { ["order_id"] = orderId.ToString(), ["item_id"] = article.ToString(), ["edited"] = "1", ["title"] = "Choosing a bouquet", ["description"] = "A quick guide", ["body"] = "## Intro\nText" });
        Assert.Equal(HttpStatusCode.OK, ps);
        Assert.Equal("https://seo-a.oxira.sa/blog/choosing-a-bouquet", pub.Str("url"));
        var post = JsonNode.Parse(app.Kv.Keys[$"post:o{orderId}:choosing-a-bouquet"])!;
        Assert.Equal("## Intro\nText", (string?)post["body"]);
        var meta = items.First(i => i.Str("kind") == "meta").GetProperty("id").GetInt32();
        await c.PostJson("/api/seo/publish", new { order_id = orderId, item_id = meta });
        var seo = JsonNode.Parse(app.Kv.Keys[$"seo:o{orderId}"])!;
        Assert.Equal("ورد الرياض | تنسيق زهور", (string?)seo["meta"]!["title"]);
        Assert.Equal("choosing-a-bouquet", (string?)seo["posts"]![0]!["slug"]);
        var local = items.First(i => i.Str("kind") == "local").GetProperty("id").GetInt32();
        await c.PostJson("/api/seo/reject", new { order_id = orderId, item_id = local });
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostJson("/api/seo/publish", new { order_id = orderId, item_id = local })).Status);

        await c.PostJson("/api/seo/settings", new { order_id = orderId, services = "تنسيق زهور", keywords = "ورد الرياض\nتنسيق زهور\nورد الرياض", competitors = "", langs = new[] { "ar", "en" }, gbp_maps = "https://maps.app.goo.gl/x", gbp_manager = "1" });
        var settings = (await c.GetJson($"/api/seo/{orderId}")).Body.GetProperty("settings");
        Assert.Equal(2, settings.GetProperty("brief").GetProperty("keywords").GetArrayLength());
        Assert.Equal(1, settings.GetProperty("brief").GetProperty("langs").GetArrayLength()); // English needs SEO Growth
        Assert.True(settings.GetProperty("gbp").GetProperty("manager").GetBoolean());

        // a site hosted elsewhere publishes through WordPress
        var (etoken, eorder, _) = await Seed.SiteAsync(app, "wp@example.com", "wpsite", ["seo"], OrderKinds.External);
        var ec = app.Client(etoken);
        var (ws, wb) = await ec.PostForm("/api/seo/wordpress", new() { ["order_id"] = eorder.ToString(), ["wp_url"] = "https://wpsite.example.com/", ["wp_user"] = "admin", ["wp_pass"] = "wrong password" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ws);
        Assert.Equal("wp_auth", wb.Str("reason"));
        Assert.Equal(HttpStatusCode.OK, (await ec.PostJson("/api/seo/wordpress", new { order_id = eorder, wp_url = "https://wpsite.example.com", wp_user = "admin", wp_pass = app.Wp.Password })).Status);
        var stored = await app.Db(db => db.SeoProfiles.SingleAsync(p => p.OrderId == eorder));
        Assert.NotEqual(app.Wp.Password, stored.WpSecret);
        Assert.DoesNotContain("abcd", stored.WpSecret);
        var item = await app.Db(async db =>
        {
            var x = new SeoItem { OrderId = eorder, Kind = "article", Title = "WP article", Description = "d", Body = "## Hi\n**bold**", CreatedAt = DateTimeOffset.UtcNow };
            db.SeoItems.Add(x);
            await db.SaveChangesAsync();
            return x.Id;
        });
        var (_, wpPub) = await ec.PostJson("/api/seo/publish", new { order_id = eorder, item_id = item });
        Assert.StartsWith("https://wpsite.example.com/?p=", wpPub.Str("url"));
        Assert.Contains(app.Wp.Posts, p => p.Html == "<h2>Hi</h2><p><strong>bold</strong></p>");
        var (_, ev) = await ec.GetJson($"/api/seo/{eorder}");
        Assert.True(ev.GetProperty("settings").GetProperty("wp").GetProperty("ok").GetBoolean());
        Assert.True(ev.GetProperty("external").GetBoolean());
    }

    [Fact]
    public async Task Renewals_past_due_and_cancellation()
    {
        var (token, orderId, userId) = await Seed.SiteAsync(app, "renew@example.com", "renew-a", []);
        var today = DateOnly.FromDateTime(app.Clock.GetUtcNow().AddHours(3).DateTime);
        var ids = await app.Db(async db =>
        {
            var now = app.Clock.GetUtcNow();
            Subscription S(string p, string status, int dueIn, string period = "month", decimal price = 99) =>
                new() { OrderId = orderId, UserId = userId, Product = p, Price = price, Period = period, Status = status, NextDue = today.AddDays(dueIn), CreatedAt = now };
            var list = new[]
            {
                S("booking", SubStatuses.Active, 2), S("menu", SubStatuses.Active, -1), S("reviews", SubStatuses.PastDue, -8), S("chat", SubStatuses.Cancelling, 0),
                S("hosting", SubStatuses.PastDue, -9, "year", 499),
            };
            db.Subscriptions.AddRange(list);
            await db.SaveChangesAsync();
            return list.Select(x => x.Id).ToArray();
        });
        var staff = await app.TokenAsync("renew.staff@oxira.sa", "staff");
        var (_, job) = await app.Client(staff).PostJson("/api/admin/jobs/renewals", new { });
        Assert.True(job.GetProperty("count").GetInt32() >= 4);
        var subs = await app.Db(db => db.Subscriptions.AsNoTracking().Where(s => s.OrderId == orderId).ToDictionaryAsync(s => s.Product));
        Assert.Equal(SubStatuses.Active, subs["booking"].Status);
        Assert.NotNull(subs["booking"].PendingPaymentId);
        Assert.StartsWith("https://checkout.moyasar.test/", subs["booking"].InvoiceUrl);
        Assert.Equal(SubStatuses.PastDue, subs["menu"].Status);
        Assert.Equal(SubStatuses.Cancelled, subs["reviews"].Status);
        Assert.Equal(SubStatuses.Cancelled, subs["chat"].Status);
        Assert.Equal(SubStatuses.Cancelled, subs["hosting"].Status);
        // a cancelled hosting suspends the site
        Assert.Contains("\"suspended\":true", app.Kv.Keys[$"feat:o{orderId}"]);
        Assert.Contains("\"booking\"", app.Kv.Keys[$"feat:o{orderId}"]);
        Assert.Contains(app.Email.To("renew@example.com"), m => m.Subject.Contains("booking"));
        var bell = (await app.Client(token).GetJson("/api/account/notifications")).Body.GetProperty("items").EnumerateArray();
        Assert.Contains(bell, x => x.Str("type") == "renew" && x.Str("product") == "booking");

        // paying the renewal moves the due date a month on from the old one
        var oldDue = subs["booking"].NextDue!.Value;
        var inv = app.Gateway.For(subs["booking"].PendingPaymentId!.Value);
        app.Gateway.Pay(inv);
        await app.Client().PostJson("/api/payments/moyasar/callback", new { id = inv });
        var renewed = await app.Db(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == ids[0]));
        Assert.Equal(oldDue.AddMonths(1), renewed.NextDue);
        Assert.Null(renewed.PendingPaymentId);
        // running again issues nothing new for it
        await app.Client(staff).PostJson("/api/admin/jobs/renewals", new { });
        Assert.Null((await app.Db(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == ids[0]))).PendingPaymentId);
    }

    [Fact]
    public async Task Status_page_from_the_uptime_checks()
    {
        var staff = await app.TokenAsync("status.staff@oxira.sa", "staff");
        app.Probe.Down["https://n8n.oxira.sa/healthz"] = true;
        await app.Client(staff).PostJson("/api/admin/jobs/status", new { });
        app.Clock.Advance(TimeSpan.FromMinutes(15));
        app.Probe.Down.Clear();
        await app.Client(staff).PostJson("/api/admin/jobs/status", new { });
        var (s, st) = await app.Client().GetJson("/api/status");
        Assert.Equal(HttpStatusCode.OK, s);
        var services = st.GetProperty("services").EnumerateArray().ToDictionary(x => x.Str("key"));
        Assert.Equal(5, services.Count);
        Assert.True(services["web"].GetProperty("ok").GetBoolean());
        Assert.Equal(100, services["web"].GetProperty("uptime24h").GetDouble());
        Assert.Equal(50, services["automation"].GetProperty("uptime24h").GetDouble());
        Assert.Equal(new[] { 0, 1 }, services["automation"].GetProperty("history").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.Equal(services["automation"].GetProperty("checked").GetInt64(), services["automation"].GetProperty("since").GetInt64());
        Assert.True(st.GetProperty("updated").GetInt64() > 0);
    }
}
