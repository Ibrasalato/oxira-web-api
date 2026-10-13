using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Features.Accounts;
using Oxira.Web.Application.Features.External;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Application.Features.Public;

namespace Oxira.Web.Api.Controllers;

/// <summary>
/// The client portal (the account being worked on: one's own, or a team owner's after POST /api/auth/switch).
/// Viewers can read; changes answer 403 reason viewer.
/// </summary>
[ApiController, Route("api/portal"), Authorize]
public class PortalController(ISender mediator) : ControllerBase
{
    /// <summary>Sites, subscriptions, bookings, sold tickets and monthly content.</summary>
    [HttpGet, HttpGet("load"), HttpPost("load")]
    public Task<PortalView> Load(CancellationToken ct) => mediator.Send(new LoadPortalQuery(), ct);

    /// <summary>Add an add-on to a site → payUrl (Moyasar) for paid ones. Reasons: not_verified, not_for_external, not_live.</summary>
    [HttpPost("subscribe")]
    public Task<SubscribeResult> Subscribe(SubscribeInput body, CancellationToken ct) => mediator.Send(new SubscribeCommand(body), ct);

    /// <summary>Cancel an add-on: it keeps working until the paid period ends.</summary>
    [HttpPost("cancel")]
    public Task<Done> Cancel(CancelInput body, CancellationToken ct) => mediator.Send(new CancelSubscriptionCommand(body.SubId ?? 0), ct);

    /// <summary>Booking status: confirmed | done | cancelled.</summary>
    [HttpPost("booking")]
    public Task<Done> Booking(BookingStatusInput body, CancellationToken ct) => mediator.Send(new SetBookingStatusCommand(body), ct);

    /// <summary>Publish the menu / price list.</summary>
    [HttpPost("menu")]
    public Task<Done> Menu(MenuInput body, CancellationToken ct) => mediator.Send(new SaveMenuCommand(body), ct);

    /// <summary>Add-on settings: booking hours, Google review link, chat notes, event tickets.</summary>
    [HttpPost("settings")]
    public Task<Done> Settings(SettingsInput body, CancellationToken ct) => mediator.Send(new SaveSettingsCommand(body), ct);

    /// <summary>Check a ticket in at the door. Reasons: not_found, already_used, not_paid.</summary>
    [HttpPost("checkin")]
    public Task<CheckinResult> Checkin(CheckinInput body, CancellationToken ct) => mediator.Send(new CheckinCommand(body.Code), ct);
}

/// <summary>Bell notifications (also pushed on /hubs/account), tax invoices, support tickets, team and partner programme.</summary>
[ApiController, Route("api/account"), Authorize]
public class AccountController(ISender mediator) : ControllerBase
{
    [HttpGet("notifications"), HttpPost("notifications")]
    public Task<NotificationsResult> Notifications(CancellationToken ct) => mediator.Send(new NotificationsQuery(), ct);

    /// <summary>Mark every notification read.</summary>
    [HttpPost("notifications/read")]
    public Task<Done> ReadNotifications(CancellationToken ct) => mediator.Send(new ReadNotificationsCommand(), ct);

    [HttpGet("invoices")]
    public Task<InvoicesResult> Invoices(CancellationToken ct) => mediator.Send(new InvoicesQuery(), ct);

    /// <summary>The account's tickets; the Oxira team (admin:true) sees every ticket.</summary>
    [HttpGet("tickets")]
    public Task<TicketsResult> Tickets(CancellationToken ct) => mediator.Send(new TicketsQuery(), ct);

    /// <summary>Category: website | domain | seo | social | ads | billing | other. images: uploaded image ids (up to 4).</summary>
    [HttpPost("tickets")]
    public Task<SubmitResult> NewTicket(NewTicketInput body, CancellationToken ct) => mediator.Send(new NewTicketCommand(body), ct);

    [HttpPost("tickets/{id:int}/reply")]
    public Task<Done> Reply(int id, ReplyInput body, CancellationToken ct) => mediator.Send(new ReplyTicketCommand(body with { Id = id }), ct);

    /// <summary>read | close | reopen</summary>
    [HttpPost("tickets/{id:int}/{op:regex(^(read|close|reopen)$)}")]
    public Task<Done> TicketAction(int id, string op, CancellationToken ct) => mediator.Send(new TicketActionCommand(id, op), ct);

    [HttpGet("team")]
    public Task<TeamResult> Team(CancellationToken ct) => mediator.Send(new TeamQuery(), ct);

    /// <summary>Invite by email with role full | viewer (owner only).</summary>
    [HttpPost("team")]
    public Task<Done> Invite(InviteInput body, CancellationToken ct) => mediator.Send(new InviteMemberCommand(body), ct);

    [HttpPut("team/{id:int}")]
    public Task<Done> Role(int id, MemberRoleInput body, CancellationToken ct) => mediator.Send(new SetMemberRoleCommand(body with { Id = id }), ct);

    [HttpDelete("team/{id:int}")]
    public Task<Done> Remove(int id, CancellationToken ct) => mediator.Send(new RemoveMemberCommand(id), ct);

    /// <summary>Partner link, referrals and commission (partner: null when not a partner).</summary>
    [HttpGet("partner")]
    public Task<PartnerResult> Partner(CancellationToken ct) => mediator.Send(new PartnerQuery(), ct);
}

/// <summary>Websites hosted elsewhere (for SEO and social add-ons): add, then prove ownership with a DNS TXT record.</summary>
[ApiController, Route("api/sites/external"), Authorize]
public class ExternalSitesController(ISender mediator) : ControllerBase
{
    [HttpPost]
    public Task<AddExternalResult> Add(AddExternalInput body, CancellationToken ct) => mediator.Send(new AddExternalSiteCommand(body), ct);

    /// <summary>{verified, txt}: add the txt value as a TXT record on the domain, then check again.</summary>
    [HttpPost("{id:int}/check")]
    public Task<CheckExternalResult> Check(int id, CancellationToken ct) => mediator.Send(new CheckExternalSiteCommand(id), ct);

    [HttpPost("check"), ApiExplorerSettings(IgnoreApi = true)]
    public Task<CheckExternalResult> CheckBody(CheckExternalInput body, CancellationToken ct) => mediator.Send(new CheckExternalSiteCommand(body.OrderId ?? 0), ct);
}
