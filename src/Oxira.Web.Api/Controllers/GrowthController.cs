using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Application.Features.Seo;
using Oxira.Web.Application.Features.Sites;
using Oxira.Web.Application.Features.Social;
using Oxira.Web.Application.Features.Status;

namespace Oxira.Web.Api.Controllers;

/// <summary>The SEO add-on: suggestions to approve, reports, the brief and the WordPress connection (sites hosted elsewhere).</summary>
[ApiController, Route("api/seo"), Authorize]
public class SeoController(ISender mediator) : ControllerBase
{
    /// <summary>{items, report, history, ranks, health, leadsNow, settings{brief, gbp, wp}}</summary>
    [HttpGet("{orderId:int}")]
    public Task<SeoView> List(int orderId, CancellationToken ct) => mediator.Send(new SeoListQuery(orderId), ct);

    [HttpPost("list"), ApiExplorerSettings(IgnoreApi = true)]
    public Task<SeoView> ListPost(SeoOrderInput body, CancellationToken ct) => mediator.Send(new SeoListQuery(body.OrderId ?? 0), ct);

    /// <summary>Approve (optionally edited) and publish. Errors for WordPress: wp_url, wp_auth, wp_rights, wp_unreachable, wp_failed.</summary>
    [HttpPost("publish")]
    public Task<SeoPublishResult> Publish(SeoDecisionInput body, CancellationToken ct) => mediator.Send(new SeoPublishCommand(body), ct);

    [HttpPost("reject")]
    public Task<Done> Reject(SeoDecisionInput body, CancellationToken ct) => mediator.Send(new SeoRejectCommand(body), ct);

    /// <summary>The brief (services, areas, keywords one per line …) and the Google Business Profile.</summary>
    [HttpPost("settings")]
    public Task<Done> Settings(SeoBriefInput body, CancellationToken ct) => mediator.Send(new SeoSaveBriefCommand(body), ct);

    /// <summary>Connect WordPress with an application password (stored encrypted).</summary>
    [HttpPost("wordpress")]
    public Task<Done> Wordpress(WpConnectInput body, CancellationToken ct) => mediator.Send(new WpConnectCommand(body), ct);

    [HttpPost("wordpress/disconnect")]
    public Task<Done> WordpressOff(SeoOrderInput body, CancellationToken ct) => mediator.Send(new WpDisconnectCommand(body.OrderId ?? 0), ct);
}

/// <summary>The social media add-on: Meta connection, the brief, the monthly plan (approve / reject) and the report.</summary>
[ApiController, Route("api/social"), Authorize]
public class SocialController(ISender mediator, Microsoft.Extensions.Options.IOptions<SiteOptions> site) : ControllerBase
{
    [HttpGet("{orderId:int}")]
    public Task<SocialView> List(int orderId, CancellationToken ct) => mediator.Send(new SocialListQuery(orderId), ct);

    [HttpPost("list"), ApiExplorerSettings(IgnoreApi = true)]
    public Task<SocialView> ListPost(SocialOrderInput body, CancellationToken ct) => mediator.Send(new SocialListQuery(body.OrderId ?? 0), ct);

    /// <summary>{url}: send the browser to Facebook Login. Reason meta_not_ready while the Meta app is not configured.</summary>
    [HttpPost("meta/start")]
    public Task<UrlResult> MetaStart(SocialOrderInput body, CancellationToken ct) => mediator.Send(new MetaStartCommand(body), ct);

    /// <summary>Facebook Login redirect: stores the pages, then back to the account page (?social=connected | error).</summary>
    [HttpGet("meta/callback"), AllowAnonymous]
    public async Task<IActionResult> MetaCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        var back = await mediator.Send(new MetaCallbackCommand(code, state, error), ct);
        return Redirect(back.StartsWith(site.Value.SiteUrl, StringComparison.OrdinalIgnoreCase) ? back : site.Value.SiteUrl);
    }

    [HttpPost("meta/disconnect")]
    public Task<Done> MetaDisconnect(SocialOrderInput body, CancellationToken ct) => mediator.Send(new MetaDisconnectCommand(body.OrderId ?? 0), ct);

    [HttpPost("meta/select")]
    public Task<Done> MetaSelect(SocialSelectInput body, CancellationToken ct) => mediator.Send(new MetaSelectCommand(body), ct);

    [HttpPost("brief")]
    public Task<Done> Brief(SocialBriefInput body, CancellationToken ct) => mediator.Send(new SocialSaveBriefCommand(body), ct);

    /// <summary>Approve a post (with its final image), reel or ad. Reasons: over_cap, past, no_image, not_subscribed, meta_not_ready, no_ad_account, post_not_published, no_link.</summary>
    [HttpPost("approve")]
    public Task<Done> Approve(SocialDecisionInput body, CancellationToken ct) => mediator.Send(new SocialApproveCommand(body), ct);

    [HttpPost("reject")]
    public Task<Done> Reject(SocialDecisionInput body, CancellationToken ct) => mediator.Send(new SocialRejectCommand(body), ct);
}

[ApiController, Route("api/status")]
public class StatusController(ISender mediator) : ControllerBase
{
    /// <summary>Uptime of the Oxira services (checked every 15 minutes): {updated, interval, services[{key, ok, since, checked, uptime24h, checks, history}]}.</summary>
    [HttpGet]
    public async Task<StatusView> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await mediator.Send(new StatusQuery(), ct);
    }
}

/// <summary>Scheduled jobs on demand (Oxira team). They also run by themselves.</summary>
[ApiController, Route("api/admin/jobs"), Authorize(Policy = "staff")]
public class JobsController(ISender mediator) : ControllerBase
{
    /// <summary>renewals | seo | social | content | publish | status</summary>
    [HttpPost("{name}")]
    public async Task<object> Run(string name, CancellationToken ct) => new
    {
        success = true,
        count = name switch
        {
            "renewals" => await mediator.Send(new RunRenewalsCommand(), ct),
            "seo" => await mediator.Send(new DraftSeoCommand(), ct),
            "social" => await mediator.Send(new DraftSocialCommand(), ct),
            "content" => await mediator.Send(new DraftContentCommand(), ct),
            "publish" => await mediator.Send(new PublishDueSocialCommand(), ct),
            "status" => await mediator.Send(new CheckServicesCommand(), ct),
            _ => throw Oxira.Web.Domain.DomainException.NotFound("مهمة غير معروفة"),
        },
    };
}

/// <summary>
/// Forms on the client websites (posted by browsers through the sites worker): contact, booking, ticket, review.
/// They answer with a redirect (Google review, payment page, ticket page) or a small page with a link back to the site.
/// </summary>
[ApiController, Route("api/sites")]
public class ClientSitesController(ISender mediator) : ControllerBase
{
    private IActionResult Page(SiteFormResult r)
    {
        if (r.Redirect != null) return Redirect(r.Redirect);
        var ar = r.Lang == "ar";
        var e = (string s) => System.Net.WebUtility.HtmlEncode(s);
        var html = $"""
            <!doctype html><html lang="{e(r.Lang)}" dir="{(ar ? "rtl" : "ltr")}"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="robots" content="noindex"><title>{e(r.Title)}</title>
            <body style="font-family:system-ui,Tahoma,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0;background:#F5F7F9;color:#0A253E;text-align:center">
            <main style="max-width:420px;padding:24px"><p style="font-size:44px;margin:0">{(r.Ok ? "✓" : "!")}</p><h1 style="margin:8px 0">{e(r.Title)}</h1>
            <p style="color:#556779">{e(r.Body)}</p><p><a href="{e(r.BackUrl)}" style="color:#007DB4">{(ar ? "العودة إلى الموقع" : "Back to the site")}</a></p></main></body></html>
            """;
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = r.Ok ? 200 : 400 };
    }

    [HttpPost("contact"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Contact(SiteContactInput body, CancellationToken ct) => Page(await mediator.Send(new SiteContactCommand(body), ct));

    [HttpPost("booking"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Booking(SiteBookingInput body, CancellationToken ct) => Page(await mediator.Send(new SiteBookingCommand(body), ct));

    /// <summary>Free tickets go straight to the ticket page; paid ones to Moyasar first.</summary>
    [HttpPost("ticket"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Ticket(SiteTicketInput body, CancellationToken ct) => Page(await mediator.Send(new SiteTicketCommand(body), ct));

    /// <summary>4–5 stars continue to the business's Google review page; lower ratings reach the owner privately.</summary>
    [HttpPost("review"), EnableRateLimiting("forms")]
    public async Task<IActionResult> Review(SiteReviewInput body, CancellationToken ct) => Page(await mediator.Send(new SiteReviewCommand(body), ct));

    /// <summary>The client site's AI assistant (chat add-on): {reply}.</summary>
    [HttpPost("chat"), EnableRateLimiting("chat")]
    public Task<SiteChatReply> Chat(SiteChatInput body, CancellationToken ct) => mediator.Send(new SiteChatCommand(body), ct);
}
