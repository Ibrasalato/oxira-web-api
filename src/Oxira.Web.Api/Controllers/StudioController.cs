using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oxira.Web.Application.Features.Orders;
using Oxira.Web.Application.Features.Studio;
using Oxira.Web.Infrastructure.Services;

namespace Oxira.Web.Api.Controllers;

/// <summary>Oxira Studio: the AI designer, image uploads and shareable previews.</summary>
[ApiController, Route("api/studio")]
public class StudioController(ISender mediator) : ControllerBase
{
    /// <summary>The AI designer edits the spec: action create | edit. Returns only the changed fields (merge them), or unavailable:true.</summary>
    [HttpPost("builder"), EnableRateLimiting("chat")]
    public Task<BuilderResult> Builder(BuilderInput body, CancellationToken ct) => mediator.Send(new BuildSiteCommand(body), ct);

    /// <summary>multipart: file (JPEG, PNG or WebP, up to 8 MB) and sessionId → id (i_…_j | i_…_p), served at /api/images/{id} and by the sites worker.</summary>
    [HttpPost("uploads"), EnableRateLimiting("upload"), RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<UploadResult> Upload(IFormFile? file, [FromForm] string? sessionId, CancellationToken ct)
    {
        if (file == null) throw new Oxira.Web.Domain.DomainException("اختر صورة", "file");
        if (file.Length > 8 * 1024 * 1024) throw new Oxira.Web.Domain.DomainException("الصورة كبيرة جدًا", "too_big", 413);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await mediator.Send(new UploadImageCommand(ms.ToArray(), sessionId), ct);
    }

    /// <summary>Save a preview (spec as JSON text) → its link on the sites worker (/p/d_…/). Pass draftId to update the same link.</summary>
    [HttpPost("share"), EnableRateLimiting("forms")]
    public Task<ShareResult> Share(ShareInput body, CancellationToken ct) => mediator.Send(new ShareDraftCommand(body), ct);

    [HttpGet("drafts/{id}")]
    public Task<DraftDto> Draft(string id, CancellationToken ct) => mediator.Send(new GetDraftQuery(id), ct);
}

[ApiController, Route("api/images")]
public class ImagesController(ISender mediator) : ControllerBase
{
    /// <summary>An uploaded image (cached for a year: ids never change).</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var f = await mediator.Send(new GetImageQuery(id), ct);
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.AccessControlAllowOrigin = "*";
        return File(f.Content, f.Mime);
    }
}

/// <summary>Orders and payment (Moyasar). Prices are computed by the API; a page's total is never charged.</summary>
[ApiController, Route("api/orders")]
public class OrdersController(ISender mediator) : ControllerBase
{
    /// <summary>A studio website order (plan in addons: plan:starter|pro|business, plus "domain"). Then POST checkout.</summary>
    [HttpPost("site"), EnableRateLimiting("forms")]
    public Task<OrderCreated> Site(SiteOrderInput body, CancellationToken ct) => mediator.Send(new CreateSiteOrderCommand(body), ct);

    /// <summary>The "online in a day" bundle (3,999 SAR + VAT): creates the order and the payment page (payUrl null when payments are down).</summary>
    [HttpPost("bundle"), EnableRateLimiting("forms")]
    public Task<BundleResult> Bundle(BundleInput body, CancellationToken ct) => mediator.Send(new CreateBundleOrderCommand(body), ct);

    /// <summary>The Moyasar payment page for an order; afterwards the client returns to returnUrl?paid=&lt;id&gt; (or ?cancel=&lt;id&gt;).</summary>
    [HttpPost("checkout"), EnableRateLimiting("forms")]
    public Task<CheckoutResult> Checkout(CheckoutInput body, CancellationToken ct) => mediator.Send(new CheckoutCommand(body), ct);

    [HttpPost("{id:int}/checkout"), EnableRateLimiting("forms"), ApiExplorerSettings(IgnoreApi = true)]
    public Task<CheckoutResult> CheckoutById(int id, CheckoutInput? body, CancellationToken ct) => mediator.Send(new CheckoutCommand(new CheckoutInput(id, body?.ReturnUrl)), ct);

    /// <summary>{status: paid | pending}. Also pushed live on /hubs/orders (WatchOrder(id) → "payment").</summary>
    [HttpGet("{id:int}/payment")]
    public Task<PaymentStatusResult> Status(int id, CancellationToken ct) => mediator.Send(new PaymentStatusQuery(id), ct);

    [HttpPost("payment-status"), ApiExplorerSettings(IgnoreApi = true)]
    public Task<PaymentStatusResult> StatusPost(OrderIdInput body, CancellationToken ct) => mediator.Send(new PaymentStatusQuery(body.OrderId ?? 0), ct);
}

/// <summary>Gateway callbacks. Nothing is trusted from the request itself: the invoice is read back from Moyasar.</summary>
[ApiController, Route("api/payments")]
public class PaymentsController(ISender mediator) : ControllerBase
{
    /// <summary>Moyasar callback_url / webhook (invoice or payment JSON).</summary>
    [HttpPost("moyasar/callback")]
    public async Task<Oxira.Web.Application.Common.Done> Callback(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        return await mediator.Send(new PaymentNotificationCommand(body), ct);
    }

    [HttpGet("moyasar/callback"), ApiExplorerSettings(IgnoreApi = true)]
    public async Task<Oxira.Web.Application.Common.Done> CallbackGet([FromQuery] string? id, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(id)) await mediator.Send(new ConfirmPaymentCommand(id), ct);
        return new Oxira.Web.Application.Common.Done();
    }

    /// <summary>Payments:Simulate only — the stand-in payment page (?pay=1 pays, ?pay=0 cancels).</summary>
    [HttpGet("simulate/{id}"), ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Simulate(string id, [FromQuery] int? pay, CancellationToken ct)
    {
        var sim = HttpContext.RequestServices.GetService<IPaymentSimulator>();
        var inv = sim?.Find(id);
        if (sim == null || inv == null) return NotFound();
        if (pay == 1)
        {
            sim.MarkPaid(id);
            await mediator.Send(new ConfirmPaymentCommand(id), ct);
            return Redirect(inv.SuccessUrl);
        }
        if (pay == 0) return Redirect(inv.BackUrl);
        var e = System.Net.WebUtility.HtmlEncode(inv.Description);
        return Content($"""
            <!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Simulated payment</title>
            <body style="font-family:system-ui;max-width:420px;margin:60px auto;text-align:center"><h1>Simulated payment</h1><p>{e}</p>
            <p><b>{inv.AmountHalalas / 100m:0.00} SAR</b></p><p><a href="?pay=1">Pay</a> · <a href="?pay=0">Cancel</a></p></body>
            """, "text/html; charset=utf-8");
    }
}
