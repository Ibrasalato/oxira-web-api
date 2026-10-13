using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace Oxira.Web.Tests;

public class StudioTests(TestApp app) : IClassFixture<TestApp>
{
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    public static string Spec(string name = "مقهى الورد") => JsonSerializer.Serialize(new { template = "cafe", lang = "ar", name, hero = new { title = "قهوة مختصة", subtitle = "", cta = "اطلب" }, media = new { logo = "" } });

    public static async Task<string> UploadAsync(HttpClient c, byte[] bytes, string name = "logo.png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", name);
        form.Add(new StringContent("sess-studio-1"), "sessionId");
        var res = await c.PostAsync("/api/studio/uploads", form);
        var body = await Http.Read(res);
        return res.IsSuccessStatusCode ? body.Str("id") : body.Str("reason");
    }

    [Fact]
    public async Task Upload_serve_and_share_a_preview()
    {
        var c = app.Client();
        var id = await UploadAsync(c, Png);
        Assert.Matches("^i_[a-f0-9]{32}_p$", id);
        var img = await c.GetAsync($"/api/images/{id}");
        Assert.Equal("image/png", img.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await img.Content.ReadAsByteArrayAsync());
        Assert.True(app.Kv.Bytes.ContainsKey($"img:{id}"));
        Assert.Equal("file", await UploadAsync(c, "not an image"u8.ToArray(), "x.png"));

        var (s, share) = await c.PostForm("/api/studio/share", new() { ["spec"] = Spec(), ["draftId"] = "" });
        Assert.Equal(HttpStatusCode.OK, s);
        var draftId = share.Str("draftId");
        Assert.Matches("^d_[a-f0-9]{32}$", draftId);
        Assert.Equal($"https://oxira-sites.oxira-sa.workers.dev/p/{draftId}/", share.Str("url"));
        var (_, again) = await c.PostForm("/api/studio/share", new() { ["spec"] = Spec("مقهى الياسمين"), ["draftId"] = draftId });
        Assert.Equal(draftId, again.Str("draftId"));
        Assert.Contains("مقهى الياسمين", app.Kv.Keys[$"draft:{draftId}"]);
        var (_, d) = await c.GetJson($"/api/studio/drafts/{draftId}");
        Assert.Equal("مقهى الياسمين", d.GetProperty("spec").Str("name"));
    }

    [Fact]
    public async Task Ai_designer_returns_only_changes_and_never_the_template()
    {
        app.Ai.Script.Enqueue(FakeAnthropic.Text("""{"reply":"غيّرت العنوان","spec":{"hero":{"title":"أفضل قهوة في الرياض","subtitle":"محمصة محلية","cta":"زورونا"},"template":"law","media":{"logo":"x"}}}"""));
        var (s, r) = await app.Client().PostForm("/api/studio/builder", new() { ["sessionId"] = "sess-studio-1", ["action"] = "edit", ["message"] = "غيّر العنوان", ["spec"] = Spec(), ["lang"] = "ar" });
        Assert.Equal(HttpStatusCode.OK, s);
        Assert.True(r.GetProperty("success").GetBoolean());
        Assert.Equal("أفضل قهوة في الرياض", r.GetProperty("spec").GetProperty("hero").Str("title"));
        Assert.False(r.GetProperty("spec").TryGetProperty("template", out _));
        Assert.False(r.GetProperty("spec").TryGetProperty("media", out _));
        Assert.Equal("غيّرت العنوان", r.Str("reply"));

        app.Ai.Enabled = false;
        try
        {
            var (_, off) = await app.Client().PostForm("/api/studio/builder", new() { ["sessionId"] = "sess-studio-1", ["action"] = "create", ["message"] = "مقهى", ["spec"] = Spec() });
            Assert.True(off.GetProperty("unavailable").GetBoolean());
        }
        finally { app.Ai.Enabled = true; }
    }

    private async Task<HubConnection> WatchAsync(int orderId, TaskCompletionSource<JsonElement> paid)
    {
        var hub = new HubConnectionBuilder().WithUrl(new Uri(app.Server.BaseAddress, "hubs/orders"), o =>
        {
            o.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling;
        }).Build();
        hub.On<JsonElement>("payment", e => paid.TrySetResult(e));
        await hub.StartAsync();
        await hub.InvokeAsync("WatchOrder", orderId);
        return hub;
    }

    [Fact]
    public async Task Site_order_checkout_webhook_publish_and_live_push()
    {
        var c = app.Client();
        var (s, o) = await c.PostForm("/api/orders/site", new()
        {
            ["name"] = "Reem", ["phone"] = "0501234567", ["email"] = "reem@example.com", ["clientType"] = "business", ["template"] = "cafe", ["siteName"] = "Rose Cafe",
            ["siteLang"] = "ar", ["domainOption"] = "new", ["domainName"] = "https://www.RoseCafe.sa/", ["addons"] = "plan:starter,domain", ["total"] = "1", ["spec"] = Spec(),
            ["lang"] = "ar", ["page"] = "/studio/", ["ref"] = "",
        });
        Assert.Equal(HttpStatusCode.OK, s);
        var orderId = o.GetProperty("orderId").GetInt32();
        Assert.Equal(1598m, o.GetProperty("subtotal").GetDecimal());
        Assert.Equal(1837.7m, o.GetProperty("total").GetDecimal());

        var (cs, co) = await c.PostForm("/api/orders/checkout", new() { ["orderId"] = orderId.ToString(), ["returnUrl"] = "https://oxira.sa/studio/" });
        Assert.Equal(HttpStatusCode.OK, cs);
        Assert.StartsWith("https://checkout.moyasar.test/", co.Str("payUrl"));
        var inv = app.Gateway.Last;
        Assert.Equal(183770, app.Gateway.Invoices[inv].Amount);
        Assert.Equal($"https://oxira.sa/studio/?paid={orderId}", app.Gateway.Invoices[inv].Request.SuccessUrl);
        Assert.Equal("https://api.test/api/payments/moyasar/callback", app.Gateway.Invoices[inv].Request.CallbackUrl);
        // a foreign return address falls back to the studio
        var (_, co2) = await c.PostJson($"/api/orders/{orderId}/checkout", new { returnUrl = "https://evil.example/" });
        Assert.Equal(co.Str("payUrl"), co2.Str("payUrl"));

        var (_, pending) = await c.GetJson($"/api/orders/{orderId}/payment");
        Assert.Equal("pending", pending.Str("status"));

        // a webhook claiming payment does nothing until Moyasar says paid
        await c.PostJson("/api/payments/moyasar/callback", new { id = inv, status = "paid" });
        Assert.Equal("pending", (await c.GetJson($"/api/orders/{orderId}/payment")).Body.Str("status"));

        var paid = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = await WatchAsync(orderId, paid);
        app.Gateway.Pay(inv);
        var (ws, _) = await c.PostJson("/api/payments/moyasar/callback", new { id = inv, status = "paid" });
        Assert.Equal(HttpStatusCode.OK, ws);
        var pushed = await paid.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(orderId, pushed.GetProperty("orderId").GetInt32());
        Assert.Equal("paid", pushed.Str("status"));
        Assert.Equal("paid", (await c.PostForm("/api/orders/payment-status", new() { ["orderId"] = orderId.ToString() })).Body.Str("status"));

        // published to the sites store, with the order id the worker reads
        var spec = JsonNode.Parse(app.Kv.Keys["slug:rose-cafe"])!;
        Assert.Equal(orderId, (int)spec["_order"]!);
        Assert.True(app.Kv.Keys.ContainsKey("host:rosecafe.sa"));
        Assert.Contains("\"suspended\":false", app.Kv.Keys[$"feat:o{orderId}"]);
        var order = await app.Db(db => db.Orders.SingleAsync(x => x.Id == orderId));
        Assert.Equal("published", order.Status);
        Assert.Equal("https://rose-cafe.oxira.sa/", order.Url);
        Assert.NotNull(order.UserId);
        var subs = await app.Db(db => db.Subscriptions.Where(x => x.OrderId == orderId).Select(x => x.Product).OrderBy(x => x).ToListAsync());
        Assert.Equal(new[] { "domain", "hosting" }, subs);
        var invoice = await app.Db(db => db.Invoices.SingleAsync(x => x.OrderId == orderId));
        Assert.Matches(@"^OX-\d{4}-\d{6}$", invoice.Number);
        Assert.Equal(239.7m, invoice.Vat);
        // the domain is registered only now, after a confirmed payment that included it
        Assert.Contains(app.Email.Sent, m => m.To.Contains("info@oxira.sa") && m.Html.Contains("سجّل الدومين") && m.Html.Contains("rosecafe.sa"));
        Assert.Contains(app.Email.To("reem@example.com"), m => m.Subject.Contains("تم الدفع"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostJson("/api/orders/checkout", new { orderId, returnUrl = "https://oxira.sa/studio/" })).Status);

        // the client can create an account with the order's email and sees the site
        var auth = await app.SignUpAsync("reem@example.com");
        Assert.True(auth.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Wrong_amount_is_never_accepted_and_bundle_pays_via_status()
    {
        var c = app.Client();
        var (_, o) = await c.PostJson("/api/orders/site", new { name = "Ali", phone = "0501112223", email = "ali@example.com", addons = "plan:pro", domainOption = "own", domainName = "ali.com", spec = Spec("Ali Law") });
        var orderId = o.GetProperty("orderId").GetInt32();
        Assert.Equal(2499m, o.GetProperty("subtotal").GetDecimal());
        await c.PostJson("/api/orders/checkout", new { orderId, returnUrl = "https://oxira.sa/studio/" });
        app.Gateway.Pay(app.Gateway.Last, amount: 100);
        Assert.Equal("pending", (await c.GetJson($"/api/orders/{orderId}/payment")).Body.Str("status"));

        var (bs, b) = await c.PostJson("/api/orders/bundle", new { name = "Sami", business = "Sami Bakery", phone = "0509998887", email = "sami@example.com", lang = "en", returnUrl = "https://oxira.sa/en/online-in-a-day/" });
        Assert.Equal(HttpStatusCode.OK, bs);
        var id = b.GetProperty("id").GetInt32();
        Assert.StartsWith("https://checkout.moyasar.test/", b.Str("payUrl"));
        var inv = app.Gateway.Last;
        Assert.Equal(459885, app.Gateway.Invoices[inv].Amount);
        Assert.Equal($"https://oxira.sa/en/online-in-a-day/?paid={id}", app.Gateway.Invoices[inv].Request.SuccessUrl);
        app.Gateway.Pay(inv);
        // no webhook: the page asking for the status confirms it with the gateway
        Assert.Equal("paid", (await c.GetJson($"/api/orders/{id}/payment")).Body.Str("status"));
        var subs = await app.Db(db => db.Subscriptions.Where(x => x.OrderId == id).Select(x => x.Product).ToListAsync());
        Assert.Equal(new[] { "hosting" }, subs);

        app.Gateway.Down = true;
        try
        {
            var (_, down) = await c.PostJson("/api/orders/bundle", new { name = "Sami", business = "Sami Bakery", phone = "0509998887", email = "sami@example.com" });
            Assert.True(down.GetProperty("success").GetBoolean());
            Assert.Equal(JsonValueKind.Null, down.GetProperty("payUrl").ValueKind);
        }
        finally { app.Gateway.Down = false; }
    }
}
