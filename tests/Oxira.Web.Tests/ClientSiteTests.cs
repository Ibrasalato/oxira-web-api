using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Oxira.Web.Tests;

public class ClientSiteTests(TestApp app) : IClassFixture<TestApp>
{
    private const string Settings = """
        {"capacity":1,"closedDays":[5],"googleReviewUrl":"https://g.page/r/rose/review","chatNotes":"Delivery is free over 200 SAR.","eventName":"Rose Expo",
         "tickets":[{"id":"t1","name":"VIP","price":115,"capacity":3},{"id":"t0","name":"Free entry","price":0,"capacity":0}]}
        """;

    private static async Task<string> Html(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    [Fact]
    public async Task Contact_booking_tickets_reviews_and_chat_on_a_client_site()
    {
        var (token, orderId, _) = await Seed.SiteAsync(app, "rose@example.com", "rose-site", ["booking", "tickets", "reviews", "chat"], settings: Settings);
        var web = app.NoRedirect();
        FormUrlEncodedContent Form(params (string, string)[] f) => new(f.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).Prepend(new("site", orderId.ToString())));

        var contact = await web.PostAsync("/api/sites/contact", Form(("lang", "ar"), ("name", "سلمى"), ("phone", "0501231234"), ("message", "هل عندكم توصيل؟")));
        Assert.Equal(HttpStatusCode.OK, contact.StatusCode);
        Assert.Contains("تم إرسال رسالتك", await Html(contact));
        Assert.Contains(app.Email.To("rose@example.com"), m => m.Html.Contains("هل عندكم توصيل؟"));
        var bad = await web.PostAsync("/api/sites/contact", Form(("lang", "en"), ("name", ""), ("phone", "1"), ("message", "")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Back to the site", await Html(bad));

        // booking: one per slot (capacity 1), not on closed days (5 = Friday)
        var booking = await web.PostAsync("/api/sites/booking", Form(("lang", "en"), ("service", "Bouquet"), ("date", "2026-10-20"), ("time", "10:00"), ("name", "Sara"), ("phone", "0505556666"), ("email", "sara@example.com")));
        Assert.Equal(HttpStatusCode.OK, booking.StatusCode);
        var again = await web.PostAsync("/api/sites/booking", Form(("lang", "en"), ("date", "2026-10-20"), ("time", "10:00"), ("name", "Ali"), ("phone", "0505557777")));
        Assert.Contains("taken", await Html(again));
        var friday = await web.PostAsync("/api/sites/booking", Form(("lang", "en"), ("date", "2026-10-16"), ("time", "10:00"), ("name", "Ali"), ("phone", "0505557777")));
        Assert.Contains("Closed", await Html(friday));
        Assert.Contains((await app.Client(token).GetJson("/api/account/notifications")).Body.GetProperty("items").EnumerateArray(), x => x.Str("type") == "booking");
        Assert.Contains(app.Email.To("sara@example.com"), m => m.Html.Contains("2026-10-20"));

        // free ticket: straight to the ticket page
        var free = await web.PostAsync("/api/sites/ticket", Form(("lang", "en"), ("type", "t0"), ("name", "Omar"), ("phone", "0501110000"), ("email", "omar@example.com"), ("qty", "2")));
        Assert.Equal(HttpStatusCode.Redirect, free.StatusCode);
        var code = free.Headers.Location!.ToString().Split('/').Last();
        Assert.StartsWith("https://rose-site.oxira.sa/ticket/", free.Headers.Location!.ToString());
        var kvTicket = JsonNode.Parse(app.Kv.Keys[$"ticket:{code}"])!;
        Assert.Equal("paid", (string?)kvTicket["status"]);
        Assert.Equal("Rose Expo", (string?)kvTicket["event"]);

        // paid ticket: Moyasar first, issued when the invoice is paid
        var paid = await web.PostAsync("/api/sites/ticket", Form(("lang", "en"), ("type", "t1"), ("name", "Huda"), ("phone", "0501112222"), ("email", "huda@example.com"), ("qty", "2")));
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        Assert.StartsWith("https://checkout.moyasar.test/", paid.Headers.Location!.ToString());
        var inv = app.Gateway.Last;
        Assert.Equal(23000, app.Gateway.Invoices[inv].Amount);
        var pending = await app.Db(db => db.EventTickets.AsNoTracking().SingleAsync(t => t.Email == "huda@example.com"));
        Assert.Equal("pending", pending.Status);
        Assert.Equal($"https://rose-site.oxira.sa/ticket/{pending.Code}", app.Gateway.Invoices[inv].Request.SuccessUrl);
        // capacity 3: two more would be too many while these two are being paid
        var soldOut = await web.PostAsync("/api/sites/ticket", Form(("lang", "en"), ("type", "t1"), ("name", "Late"), ("phone", "0501113333"), ("email", "late@example.com"), ("qty", "2")));
        Assert.Contains("Sold out", await Html(soldOut));
        app.Gateway.Pay(inv);
        await app.Client().PostJson("/api/payments/moyasar/callback", new { id = inv });
        Assert.Equal("paid", (await app.Db(db => db.EventTickets.AsNoTracking().SingleAsync(t => t.Id == pending.Id))).Status);
        Assert.Contains("\"t1\":2", app.Kv.Keys[$"feat:o{orderId}"]);
        Assert.Contains(app.Email.To("huda@example.com"), m => m.Html.Contains($"/ticket/{pending.Code}"));
        Assert.False(await app.Db(db => db.Invoices.AnyAsync(i => i.Kind == "ticket"))); // the owner's sale, not an Oxira invoice
        // the owner checks it in at the door
        Assert.Equal("Huda", (await app.Client(token).PostJson("/api/portal/checkin", new { code = pending.Code })).Body.GetProperty("ticket").Str("name"));

        // reviews: happy customers continue to Google, the rest reach the owner
        var five = await web.PostAsync("/api/sites/review", Form(("lang", "ar"), ("rating", "5"), ("comment", "رائع")));
        Assert.Equal("https://g.page/r/rose/review", five.Headers.Location!.ToString());
        var two = await web.PostAsync("/api/sites/review", Form(("lang", "ar"), ("rating", "2"), ("comment", "تأخر التوصيل"), ("name", "Nasser")));
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        Assert.Contains(app.Email.To("rose@example.com"), m => m.Html.Contains("تأخر التوصيل"));

        // the site's AI assistant answers from the site and the owner's notes
        app.Ai.Script.Enqueue(FakeAnthropic.Text("Yes, delivery is free over 200 SAR."));
        var (cs, chat) = await app.Client().PostJson("/api/sites/chat", new { site = orderId.ToString(), sessionId = "visitor-123", message = "Is delivery free?", lang = "en" });
        Assert.Equal(HttpStatusCode.OK, cs);
        Assert.Equal("Yes, delivery is free over 200 SAR.", chat.Str("reply"));
        Assert.Contains("Delivery is free over 200 SAR.", app.Ai.Calls[^1].System);

        // a site without the add-on refuses
        var (_, plainOrder, _) = await Seed.SiteAsync(app, "plain@example.com", "plain-site", []);
        var noBooking = await web.PostAsync("/api/sites/booking", new FormUrlEncodedContent(new Dictionary<string, string> { ["site"] = plainOrder.ToString(), ["lang"] = "en", ["date"] = "2026-10-20", ["time"] = "10:00", ["name"] = "A", ["phone"] = "0500000000" }));
        Assert.Contains("not available", await Html(noBooking));
    }
}
