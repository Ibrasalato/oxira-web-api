using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Tests;

public class SocialTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Meta_connect_plan_approve_and_scheduled_publishing()
    {
        var (token, orderId, _) = await Seed.SiteAsync(app, "social@example.com", "social-a", ["social"]);
        var c = app.Client(token);

        // Facebook Login: start → Meta → our callback → back to the account page
        var (ms, start) = await c.PostJson("/api/social/meta/start", new { order_id = orderId, lang = "en" });
        Assert.Equal(HttpStatusCode.OK, ms);
        var url = new Uri(start.Str("url"));
        var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"]!;
        Assert.Equal("https://api.test/api/social/meta/callback", System.Web.HttpUtility.ParseQueryString(url.Query)["redirect_uri"]);
        var cb = await app.NoRedirect().GetAsync($"/api/social/meta/callback?code=abc&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Assert.Equal("https://oxira.sa/en/account/?social=connected#social", cb.Headers.Location!.ToString());
        var tampered = await app.NoRedirect().GetAsync($"/api/social/meta/callback?code=abc&state=xyz");
        Assert.Contains("social=error", tampered.Headers.Location!.ToString());

        var (_, list) = await c.PostForm("/api/social/list", new() { ["order_id"] = orderId.ToString(), ["data"] = "{}" });
        var meta = list.GetProperty("settings").GetProperty("meta");
        Assert.True(meta.GetProperty("connected").GetBoolean());
        Assert.Equal("p1", meta.Str("page_id"));
        Assert.Equal("rose.ig", meta.Str("ig_username"));
        Assert.Equal("basic", list.Str("plan"));
        Assert.True(list.GetProperty("metaReady").GetBoolean());
        var secret = await app.Db(db => db.SocialProfiles.SingleAsync(p => p.OrderId == orderId));
        Assert.DoesNotContain("page-token", secret.MetaSecret);
        Assert.DoesNotContain("page-token", secret.MetaJson);

        await c.PostJson("/api/social/brief", new { order_id = orderId, about = "متجر ورد", voice = "friendly", time = "20:00", platforms = new[] { "instagram" }, photos = new[] { "i_0123456789abcdef0123_j", "nope" }, competitors = "Floward\nFlowers Co", replies = "1" });

        // the monthly plan from Claude
        app.Ai.Script.Enqueue(FakeAnthropic.Text(JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new { kind = "post", day = "2026-10-20", title = "ورد الخريف", caption = "باقات جديدة", hashtags = "#ورد", idea = "صورة باقة" },
                new { kind = "post", day = "2026-10-25", title = "توصيل", caption = "نفس اليوم", hashtags = "#الرياض", idea = "" },
                new { kind = "reel", day = "2026-10-22", title = "تنسيق", caption = "", hashtags = "", idea = "script" },
                new { kind = "ad", day = "2026-10-21", title = "Ad", caption = "", hashtags = "", idea = "" },
            },
        })));
        var staff = await app.TokenAsync("social.staff@oxira.sa", "staff");
        var (_, job) = await app.Client(staff).PostJson("/api/admin/jobs/social", new { });
        Assert.Equal(2, job.GetProperty("count").GetInt32()); // no reels on the basic plan, no ads without the ads add-on
        var items = (await c.GetJson($"/api/social/{orderId}")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, x => Assert.Equal("20:00", x.Str("time")));
        Assert.All(items, x => Assert.Equal("instagram", x.Str("platforms")));
        Assert.All(items, x => Assert.Equal("i_0123456789abcdef0123_j", x.Str("bg")));
        Assert.Contains((await c.GetJson("/api/account/notifications")).Body.GetProperty("items").EnumerateArray(), x => x.Str("type") == "social" && x.GetProperty("n").GetInt32() == 2);

        var first = items.First(x => x.Str("day") == "2026-10-20").GetProperty("id").GetInt32();
        Assert.Equal("no_image", (await c.PostJson("/api/social/approve", new { order_id = orderId, item_id = first, title = "ورد الخريف" })).Body.Str("reason"));
        Assert.Equal("past", (await c.PostJson("/api/social/approve", new { order_id = orderId, item_id = first, image = "i_0123456789abcdef0123_j", day = "2026-10-01", time = "10:00" })).Body.Str("reason"));
        var (ap, _) = await c.PostForm("/api/social/approve", new()
        {
            ["data"] = JsonSerializer.Serialize(new { order_id = orderId, item_id = first, title = "ورد الخريف 🌸", caption = "باقات الخريف وصلت", hashtags = "#ورد #الرياض", day = "2026-10-20", time = "19:00", platforms = new[] { "instagram", "facebook" }, image = "i_abcdefabcdefabcdefab_j" }),
        });
        Assert.Equal(HttpStatusCode.OK, ap);
        var second = items.First(x => x.Str("day") == "2026-10-25").GetProperty("id").GetInt32();
        await c.PostJson("/api/social/reject", new { order_id = orderId, item_id = second });

        // nothing is due yet; on the day at 19:00 Riyadh it is published
        Assert.Equal(0, (await app.Client(staff).PostJson("/api/admin/jobs/publish", new { })).Body.GetProperty("count").GetInt32());
        app.Clock.Advance(new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.Zero) - app.Clock.GetUtcNow());
        Assert.Equal(1, (await app.Client(staff).PostJson("/api/admin/jobs/publish", new { })).Body.GetProperty("count").GetInt32());
        var published = app.Meta.Published.Single();
        Assert.Equal("p1", published.Page);
        Assert.Equal("https://api.test/api/images/i_abcdefabcdefabcdefab_j", published.Image);
        Assert.Equal("باقات الخريف وصلت\n\n#ورد #الرياض", published.Caption);
        var done = (await c.GetJson($"/api/social/{orderId}")).Body.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetInt32());
        Assert.Equal("published", done[first].Str("status"));
        Assert.Equal("fb_1", done[first].GetProperty("result").Str("fb_id"));
        Assert.Equal("rejected", done[second].Str("status"));

        // ads need the ads add-on
        var adId = await app.Db(async db =>
        {
            var x = new SocialItem { OrderId = orderId, Month = "2026-10", Kind = "ad", Title = "Ad", AdJson = "{\"budget\":500}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.SocialItems.Add(x);
            await db.SaveChangesAsync();
            return x.Id;
        });
        Assert.Equal("not_subscribed", (await c.PostJson("/api/social/approve", new { order_id = orderId, item_id = adId })).Body.Str("reason"));
        await app.Db(async db =>
        {
            db.Subscriptions.Add(new Subscription { OrderId = orderId, Product = "ads", Price = 999, Status = SubStatuses.Active, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        await c.PostJson("/api/social/brief", new { order_id = orderId, about = "متجر ورد", ads_cap = 300 });
        Assert.Equal("over_cap", (await c.PostJson("/api/social/approve", new { order_id = orderId, item_id = adId })).Body.Str("reason"));
        Assert.Equal(HttpStatusCode.OK, (await c.PostJson("/api/social/approve", new { order_id = orderId, item_id = adId, budget = 250, days = 7, text = "عرض", headline = "ورد" })).Status);
        Assert.Contains(app.Email.Sent, m => m.To.Contains("info@oxira.sa") && m.Subject.Contains("إعلان معتمد"));

        // disconnecting removes the tokens
        await c.PostJson("/api/social/meta/disconnect", new { order_id = orderId });
        Assert.Equal("", (await app.Db(db => db.SocialProfiles.SingleAsync(p => p.OrderId == orderId))).MetaSecret);
    }
}
