using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Tests;

public class PortalTests(TestApp app) : IClassFixture<TestApp>
{
    /// <summary>a client with a paid, published site</summary>
    private async Task<(JsonElement Auth, int OrderId)> ClientWithSiteAsync(string email, string slug)
    {
        var auth = await app.SignUpAsync(email);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var orderId = await app.Db(async db =>
        {
            var o = new Order
            {
                Kind = OrderKinds.Site, UserId = userId, Email = email, Name = "Client", SiteName = "Salon " + slug, Slug = slug, Url = $"https://{slug}.oxira.sa/",
                Status = OrderStatuses.Published, PaidAt = DateTimeOffset.UtcNow, Plan = "pro", SpecJson = StudioTests.Spec(), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Orders.Add(o);
            await db.SaveChangesAsync();
            return o.Id;
        });
        return (auth, orderId);
    }

    private HubConnection Hub(string token) => new HubConnectionBuilder().WithUrl(new Uri(app.Server.BaseAddress, "hubs/account"), o =>
    {
        o.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
        o.Transports = HttpTransportType.LongPolling;
        o.AccessTokenProvider = () => Task.FromResult<string?>(token);
    }).Build();

    [Fact]
    public async Task Subscribe_pay_renewal_features_and_invoice()
    {
        var (auth, orderId) = await ClientWithSiteAsync("salon@example.com", "salon-a");
        var token = auth.Str("token");
        var c = app.Client(token);
        var (ls, view) = await c.GetJson("/api/portal");
        Assert.Equal(HttpStatusCode.OK, ls);
        var site = view.GetProperty("sites").EnumerateArray().Single();
        Assert.Equal(orderId, site.GetProperty("order_id").GetInt32());
        Assert.Equal("cafe", site.GetProperty("spec").Str("template"));
        Assert.Equal("salon@example.com", view.Str("email"));

        await using var hub = Hub(token);
        var bell = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("notification", n => bell.TrySetResult(n));
        await hub.StartAsync();

        // the website posts {k, action, data: JSON}; the API reads data's fields
        var (ss, sub) = await c.PostForm("/api/portal/subscribe", new() { ["data"] = JsonSerializer.Serialize(new { order_id = orderId, product = "booking" }) });
        Assert.Equal(HttpStatusCode.OK, ss);
        Assert.StartsWith("https://checkout.moyasar.test/", sub.Str("payUrl"));
        var inv = app.Gateway.Last;
        Assert.Equal(11385, app.Gateway.Invoices[inv].Amount); // 99 + 15% VAT
        Assert.StartsWith("https://oxira.sa/account/?paid=", app.Gateway.Invoices[inv].Request.SuccessUrl);
        var (_, pending) = await c.GetJson("/api/portal");
        var ps = pending.GetProperty("subs").EnumerateArray().Single(x => x.Str("product") == "booking");
        Assert.Equal("pending", ps.Str("status"));
        Assert.Equal(sub.Str("payUrl"), ps.Str("invoice_url"));

        app.Gateway.Pay(inv);
        await app.Client().PostJson("/api/payments/moyasar/callback", new { id = inv });
        var n = await bell.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("pay", n.Str("type"));
        Assert.Equal("booking", n.Str("product"));
        var (_, active) = await c.GetJson("/api/portal");
        var asub = active.GetProperty("subs").EnumerateArray().Single(x => x.Str("product") == "booking");
        Assert.Equal("active", asub.Str("status"));
        Assert.Equal(DateOnly.FromDateTime(app.Clock.GetUtcNow().AddHours(3).DateTime).AddMonths(1).ToString("yyyy-MM-dd"), asub.Str("next_due"));
        Assert.Equal("", asub.Str("invoice_url"));
        Assert.Contains("\"booking\"", app.Kv.Keys[$"feat:o{orderId}"]);

        var (_, invoices) = await c.GetJson("/api/account/invoices");
        var invoice = invoices.GetProperty("invoices")[0];
        Assert.Equal("booking", invoice.Str("product"));
        Assert.Equal(14.85m, invoice.GetProperty("vat").GetDecimal());
        Assert.Equal(113.85m, invoice.GetProperty("total").GetDecimal());
        var (_, notes) = await c.GetJson("/api/account/notifications");
        Assert.Contains(notes.GetProperty("items").EnumerateArray(), x => x.Str("type") == "pay" && x.Str("href") == "billing");

        // free add-on: active at once; cancel keeps it until the period ends
        var (_, free) = await c.PostJson("/api/portal/subscribe", new { order_id = orderId, product = "tickets" });
        Assert.Equal("active", free.Str("status"));
        var subId = asub.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await c.PostJson("/api/portal/cancel", new { sub_id = subId })).Status);
        var (_, after) = await c.GetJson("/api/portal");
        Assert.Equal("cancelling", after.GetProperty("subs").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == subId).Str("status"));
        // keep it (subscribe again)
        var (_, keep) = await c.PostJson("/api/portal/subscribe", new { order_id = orderId, product = "booking" });
        Assert.Equal("active", keep.Str("status"));

        // settings and menu reach the sites store
        var (bad, _) = await c.PostJson("/api/portal/settings", new { order_id = orderId, settings = new { googleReviewUrl = "javascript:alert(1)" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        await c.PostJson("/api/portal/settings", new { order_id = orderId, settings = new { bookingFrom = "10:00", bookingTo = "18:00", slotMinutes = 30, closedDays = new[] { 5 }, hacker = "x" } });
        Assert.Contains("\"bookingFrom\":\"10:00\"", app.Kv.Keys[$"feat:o{orderId}"]);
        Assert.DoesNotContain("hacker", app.Kv.Keys[$"feat:o{orderId}"]);
        await c.PostJson("/api/portal/menu", new { order_id = orderId, menu = new { title = "القائمة", note = "", categories = new[] { new { name = "قهوة", items = new[] { new { name = "لاتيه", desc = "", price = "18" } } } } } });
        Assert.Contains("لاتيه", app.Kv.Keys["slug:salon-a"]);

        // someone else's site is not found
        var other = await app.TokenAsync("stranger2@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(other).PostJson("/api/portal/subscribe", new { order_id = orderId, product = "menu" })).Status);
    }

    [Fact]
    public async Task Bookings_checkin_and_viewer_role()
    {
        var (auth, orderId) = await ClientWithSiteAsync("events@example.com", "events-a");
        var c = app.Client(auth.Str("token"));
        var ids = await app.Db(async db =>
        {
            var b = new SiteBooking { OrderId = orderId, Name = "Nora", Phone = "0500000000", Service = "Hair", Date = "2026-10-20", Time = "10:00", CreatedAt = DateTimeOffset.UtcNow };
            db.SiteBookings.Add(b);
            db.EventTickets.AddRange(
                new EventTicket { OrderId = orderId, Code = "ABC12345", Type = "t1", TypeName = "VIP", Qty = 2, Amount = 230, Name = "Fahad", Status = "paid", CreatedAt = DateTimeOffset.UtcNow },
                new EventTicket { OrderId = orderId, Code = "PEND1234", Type = "t1", TypeName = "VIP", Qty = 1, Name = "Late", Status = "pending", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return b.Id;
        });
        Assert.Equal(HttpStatusCode.OK, (await c.PostJson("/api/portal/booking", new { booking_id = ids, status = "confirmed" })).Status);
        var (_, ok) = await c.PostJson("/api/portal/checkin", new { code = "abc12345" });
        Assert.Equal("Fahad", ok.GetProperty("ticket").Str("name"));
        Assert.Equal("VIP", ok.GetProperty("ticket").Str("type"));
        Assert.Contains("\"status\":\"used\"", app.Kv.Keys["ticket:ABC12345"]);
        Assert.Equal("already_used", (await c.PostJson("/api/portal/checkin", new { code = "ABC12345" })).Body.Str("reason"));
        Assert.Equal("not_paid", (await c.PostJson("/api/portal/checkin", new { code = "PEND1234" })).Body.Str("reason"));
        Assert.Equal("not_found", (await c.PostJson("/api/portal/checkin", new { code = "NOPE0000" })).Body.Str("reason"));
        var (_, view) = await c.GetJson("/api/portal");
        Assert.Equal("confirmed", view.GetProperty("bookings")[0].Str("status"));
        Assert.Single(view.GetProperty("tickets").EnumerateArray()); // pending tickets are not listed

        // a viewer reads the owner's portal but cannot change it
        var (_, invited) = await c.PostJson("/api/account/team", new { email = "viewer@example.com", role = "viewer" });
        Assert.True(invited.GetProperty("success").GetBoolean());
        var viewer = await app.SignUpAsync("viewer@example.com");
        var (_, team) = await c.GetJson("/api/account/team");
        var member = team.GetProperty("members").EnumerateArray().Single();
        Assert.True(member.GetProperty("joined").GetBoolean());
        var (_, sw) = await app.Client(viewer.Str("token")).PostJson("/api/auth/switch", new { owner = "events@example.com" });
        var vc = app.Client(sw.Str("token"));
        var (vs, vview) = await vc.GetJson("/api/portal");
        Assert.Equal(HttpStatusCode.OK, vs);
        Assert.Equal("viewer", vview.Str("role"));
        var (fs, forbidden) = await vc.PostJson("/api/portal/subscribe", new { order_id = orderId, product = "menu" });
        Assert.Equal(HttpStatusCode.Forbidden, fs);
        Assert.Equal("viewer", forbidden.Str("reason"));
        Assert.Equal("viewer", (await vc.PostJson("/api/account/team", new { email = "x@example.com", role = "full" })).Body.Str("reason"));

        // promoted to full: can change things; removed: the switched session ends
        await c.PutJson($"/api/account/team/{member.GetProperty("id").GetInt32()}", new { role = "full" });
        Assert.Equal(HttpStatusCode.OK, (await vc.PostJson("/api/portal/booking", new { booking_id = ids, status = "done" })).Status);
        var del = await c.DeleteAsync($"/api/account/team/{member.GetProperty("id").GetInt32()}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await vc.GetJson("/api/portal")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().PostJson("/api/auth/refresh", new { refreshToken = sw.Str("refreshToken") })).Status);
    }

    [Fact]
    public async Task Support_tickets_between_client_and_team()
    {
        var (auth, orderId) = await ClientWithSiteAsync("help@example.com", "help-a");
        var client = app.Client(auth.Str("token"));
        var staffToken = await app.TokenAsync("agent@oxira.sa", "staff");
        var staff = app.Client(staffToken);

        var (ns, created) = await client.PostJson("/api/account/tickets", new { subject = "الدومين لا يعمل", category = "domain", order_id = orderId, message = "الموقع لا يفتح على الدومين", images = new[] { "i_0123456789abcdef0123_j", "bad" } });
        Assert.Equal(HttpStatusCode.OK, ns);
        var id = created.GetProperty("id").GetInt32();
        Assert.Contains(app.Email.Sent, m => m.To.Contains("info@oxira.sa") && m.Subject.Contains("T-"));

        var (_, all) = await staff.GetJson("/api/account/tickets");
        Assert.True(all.GetProperty("admin").GetBoolean());
        var t = all.GetProperty("tickets").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.True(t.GetProperty("unread").GetBoolean());
        Assert.Equal("help@example.com", t.Str("email"));
        Assert.Equal("Salon help-a", t.Str("site"));
        Assert.Single(t.GetProperty("messages")[0].GetProperty("images").EnumerateArray());

        await using var hub = Hub(auth.Str("token"));
        var live = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("ticket", e => live.TrySetResult(e));
        await hub.StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostJson($"/api/account/tickets/{id}/reply", new { message = "تم ضبط DNS، جرّب الآن" })).Status);
        var pushed = await live.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("oxira", pushed.Str("from"));
        Assert.Contains(app.Email.To("help@example.com"), m => m.Html.Contains("تم ضبط DNS"));

        var (_, mine) = await client.GetJson("/api/account/tickets");
        Assert.False(mine.GetProperty("admin").GetBoolean());
        var my = mine.GetProperty("tickets")[0];
        Assert.Equal("answered", my.Str("status"));
        Assert.True(my.GetProperty("unread").GetBoolean());
        Assert.Equal("oxira", my.GetProperty("messages")[1].Str("from"));
        var (_, bell) = await client.GetJson("/api/account/notifications");
        Assert.Contains(bell.GetProperty("items").EnumerateArray(), x => x.Str("type") == "ticket");
        await client.PostJson($"/api/account/tickets/{id}/read", new { });
        var (_, bell2) = await client.GetJson("/api/account/notifications");
        Assert.DoesNotContain(bell2.GetProperty("items").EnumerateArray(), x => x.Str("type") == "ticket");
        await client.PostJson($"/api/account/tickets/{id}/close", new { });
        Assert.Equal("closed", (await client.GetJson("/api/account/tickets")).Body.GetProperty("tickets")[0].Str("status"));
        // another client cannot see or answer it
        var other = await app.TokenAsync("nosy@example.com");
        Assert.Empty((await app.Client(other).GetJson("/api/account/tickets")).Body.GetProperty("tickets").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(other).PostJson($"/api/account/tickets/{id}/reply", new { message = "hi" })).Status);
    }

    [Fact]
    public async Task External_sites_partner_stats()
    {
        var token = await app.TokenAsync("ext@example.com");
        var c = app.Client(token);
        var (_, bb) = await c.PostForm("/api/sites/external", new() { ["domain"] = "not a domain", ["name"] = "X" });
        Assert.Equal("domain", bb.Str("reason"));
        var (_, added) = await c.PostForm("/api/sites/external", new() { ["domain"] = "https://www.Ext-Shop.com/home", ["name"] = "Ext Shop", ["lang"] = "ar" });
        var orderId = added.GetProperty("order_id").GetInt32();
        var (_, check) = await c.PostForm("/api/sites/external/check", new() { ["order_id"] = orderId.ToString() });
        Assert.False(check.GetProperty("verified").GetBoolean());
        var txt = check.Str("txt");
        Assert.StartsWith("oxira-verify=", txt);
        Assert.Equal("ibrasalato@gmail.com", check.Str("email"));

        Assert.Equal("not_verified", (await c.PostJson("/api/portal/subscribe", new { order_id = orderId, product = "seo" })).Body.Str("reason"));
        Assert.Equal("not_for_external", (await c.PostJson("/api/portal/subscribe", new { order_id = orderId, product = "booking" })).Body.Str("reason"));
        Assert.StartsWith("https://", (await c.PostJson("/api/portal/subscribe", new { order_id = orderId, product = "social" })).Body.Str("payUrl"));

        app.Dns.Txt["ext-shop.com"] = ["v=spf1 -all", $"\"{txt}\""];
        var (_, ok) = await c.PostJson($"/api/sites/external/{orderId}/check", new { });
        Assert.True(ok.GetProperty("verified").GetBoolean());
        var (_, view) = await c.GetJson("/api/portal");
        var site = view.GetProperty("sites").EnumerateArray().Single(s => s.GetProperty("order_id").GetInt32() == orderId);
        Assert.True(site.GetProperty("external").GetBoolean());
        Assert.True(site.GetProperty("verified").GetBoolean());
        Assert.StartsWith("https://", (await c.PostJson("/api/portal/subscribe", new { order_id = orderId, product = "seo" })).Body.Str("payUrl"));

        // partner dashboard: null for non-partners, then stats from referred orders
        Assert.Equal(JsonValueKind.Null, (await c.GetJson("/api/account/partner")).Body.GetProperty("partner").ValueKind);
        await app.Client().PostForm("/api/partners", new() { ["name"] = "Ext", ["phone"] = "0500000003", ["email"] = "ext@example.com" });
        var code = await app.Db(db => db.Partners.Where(p => p.Email == "ext@example.com").Select(p => p.Code).SingleAsync());
        await app.Db(async db =>
        {
            db.Orders.Add(new Order { Kind = OrderKinds.Site, Ref = code, Subtotal = 2499, Total = 2873.85m, PaidAt = DateTimeOffset.UtcNow, Status = "paid", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            db.Orders.Add(new Order { Kind = OrderKinds.Site, Ref = code, Subtotal = 1499, Total = 1723.85m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        var p = (await c.GetJson("/api/account/partner")).Body.GetProperty("partner");
        Assert.Equal(2, p.GetProperty("referrals").GetInt32());
        Assert.Equal(1, p.GetProperty("paid_orders").GetInt32());
        Assert.Equal(374.85m, p.GetProperty("due").GetDecimal());
        Assert.EndsWith($"?ref={code}", p.Str("link"));
    }
}
