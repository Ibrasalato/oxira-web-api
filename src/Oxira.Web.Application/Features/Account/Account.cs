using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;
using Oxira.Web.Application.Emails;
using Oxira.Web.Application.Features.Auth;
using Oxira.Web.Application.Features.Portal;
using Oxira.Web.Application.Features.Public;
using Oxira.Web.Domain;
using static Oxira.Web.Domain.Text;

namespace Oxira.Web.Application.Features.Accounts;

public record NotificationItem(long Id, string Type, string Product, string Site, int? N, string Date, string Subject, string Href,
    [property: JsonPropertyName("order_id")] int? OrderId, bool Read, DateTimeOffset At);
public record NotificationsResult(bool Success, List<NotificationItem> Items, int Unread);
public record InvoiceItem(string Number, [property: JsonPropertyName("paid_at")] DateTimeOffset PaidAt, string Product, string Kind, string Description, string Site,
    string Buyer, decimal Subtotal, decimal Vat, decimal Total, [property: JsonPropertyName("order_id")] int? OrderId);
public record InvoicesResult(bool Success, List<InvoiceItem> Invoices);
public record TicketMessageDto(string From, string By, DateTimeOffset At, string Text, List<string> Images);
public record TicketDto(int Id, string Number, string Subject, string Category, [property: JsonPropertyName("order_id")] int? OrderId, string Site, string Email, string Status,
    bool Unread, DateTimeOffset Updated, DateTimeOffset Created, List<TicketMessageDto> Messages);
public record TicketsResult(bool Success, bool Admin, List<TicketDto> Tickets);
public record TeamMemberDto(int Id, string Name, string Email, string Role, bool Joined);
public record TeamResult(bool Success, string Role, List<TeamMemberDto> Members);
public record PartnerDto(decimal Rate, string Code, string Link, int Referrals, [property: JsonPropertyName("paid_orders")] int PaidOrders, decimal Due, decimal Paid);
public record PartnerResult(bool Success, PartnerDto? Partner);

public record NewTicketInput(string? Subject, string? Category, [property: JsonPropertyName("order_id")] int? OrderId, string? Message, List<string>? Images, string? Lang);
public record ReplyInput(int? Id, string? Message, List<string>? Images);
public record IdInput(int? Id);
public record InviteInput(string? Email, string? Role, string? Lang);
public record MemberRoleInput(int? Id, string? Role);

public record NotificationsQuery : IQuery<NotificationsResult>;
public record ReadNotificationsCommand : ICommand<Done>;
public record InvoicesQuery : IQuery<InvoicesResult>;
public record TicketsQuery : IQuery<TicketsResult>;
public record NewTicketCommand(NewTicketInput Input) : ICommand<SubmitResult>;
public record ReplyTicketCommand(ReplyInput Input) : ICommand<Done>;
public record TicketActionCommand(int Id, string Action) : ICommand<Done>;
public record TeamQuery : IQuery<TeamResult>;
public record InviteMemberCommand(InviteInput Input) : ICommand<Done>;
public record SetMemberRoleCommand(MemberRoleInput Input) : ICommand<Done>;
public record RemoveMemberCommand(int Id) : ICommand<Done>;
public record PartnerQuery : IQuery<PartnerResult>;

/// <summary>Account extras: the bell, tax invoices, support tickets (with the Oxira team), team members and the partner dashboard.</summary>
public class AccountHandlers(IAppDbContext db, TimeProvider clock, Access access, Sessions sessions, Notifier notifier, IRealtime realtime, IEmailSender email,
    IOptions<SiteOptions> site)
    : IRequestHandler<NotificationsQuery, NotificationsResult>, IRequestHandler<ReadNotificationsCommand, Done>, IRequestHandler<InvoicesQuery, InvoicesResult>,
      IRequestHandler<TicketsQuery, TicketsResult>, IRequestHandler<NewTicketCommand, SubmitResult>, IRequestHandler<ReplyTicketCommand, Done>,
      IRequestHandler<TicketActionCommand, Done>, IRequestHandler<TeamQuery, TeamResult>, IRequestHandler<InviteMemberCommand, Done>,
      IRequestHandler<SetMemberRoleCommand, Done>, IRequestHandler<RemoveMemberCommand, Done>, IRequestHandler<PartnerQuery, PartnerResult>
{
    private DateTimeOffset Now => clock.GetUtcNow();

    // ───── bell ─────
    public async Task<NotificationsResult> Handle(NotificationsQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var since = Now.AddDays(-30);
        var ownerId = a.Owner.Id;
        var list = await db.Notifications.AsNoTracking().Where(n => n.UserId == ownerId && !n.Read && n.CreatedAt > since)
            .OrderByDescending(n => n.Id).Take(20).ToListAsync(ct);
        return new NotificationsResult(true, list.Select(n => new NotificationItem(n.Id, n.Type, n.Product, n.Site, n.N, n.Date, n.Subject, n.Href, n.OrderId, n.Read, n.CreatedAt)).ToList(), list.Count);
    }

    public async Task<Done> Handle(ReadNotificationsCommand cmd, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        await db.Notifications.Where(n => n.UserId == a.Owner.Id && !n.Read).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true), ct);
        return new Done();
    }

    // ───── invoices (ZATCA simplified tax invoices; the page prints them with the QR) ─────
    public async Task<InvoicesResult> Handle(InvoicesQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var owner = a.Owner;
        var list = await db.Invoices.AsNoTracking().Where(i => i.UserId == owner.Id || i.UserId == null && i.Email == owner.Email).OrderByDescending(i => i.Id).Take(200)
            .Select(i => new InvoiceItem(i.Number, i.PaidAt, i.Product, i.Kind, i.Description, i.Site, i.Buyer != "" ? i.Buyer : owner.Name, i.Subtotal, i.Vat, i.Total, i.OrderId))
            .ToListAsync(ct);
        return new InvoicesResult(true, list);
    }

    // ───── support tickets ─────
    private static List<string> ImagesOf(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch (JsonException) { return []; }
    }

    private static string ImagesJson(List<string>? images) => JsonSerializer.Serialize((images ?? []).Where(Images.IsId).Distinct().Take(4).ToList());

    public async Task<TicketsResult> Handle(TicketsQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        var admin = a.IsStaff && a.IsOwner;
        var query = db.SupportTickets.AsNoTracking().Include(t => t.Messages).AsQueryable();
        if (!admin) query = query.Where(t => t.OwnerId == a.Owner.Id);
        var list = await query.OrderByDescending(t => t.UpdatedAt).Take(admin ? 300 : 100).ToListAsync(ct);
        var orderIds = list.Where(t => t.OrderId != null).Select(t => t.OrderId!.Value).Distinct().ToList();
        var names = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.SiteName != "" ? o.SiteName : o.DomainName, ct);
        return new TicketsResult(true, admin, list.Select(t => new TicketDto(t.Id, t.Number, t.Subject, t.Category, t.OrderId,
            t.OrderId is { } oid && names.TryGetValue(oid, out var n) ? n : "", admin ? t.Email : "", t.Status, admin ? t.UnreadByStaff : t.UnreadByOwner, t.UpdatedAt, t.CreatedAt,
            t.Messages.OrderBy(m => m.Id).Select(m => new TicketMessageDto(m.From, m.By, m.At, m.Text, ImagesOf(m.ImagesJson))).ToList())).ToList());
    }

    public async Task<SubmitResult> Handle(NewTicketCommand cmd, CancellationToken ct)
    {
        var a = await access.WritableAsync(ct);
        var i = cmd.Input;
        var subject = Line(i.Subject, 140);
        var text = Clip(i.Message, 4000);
        if (subject == "" || text == "") throw new DomainException("اكتب الموضوع والرسالة", "bad_request");
        int? orderId = i.OrderId is > 0 ? (await PortalHandlers.OrderOfAsync(db, a, i.OrderId, ct)).Id : null;
        var t = new SupportTicket
        {
            OwnerId = a.Owner.Id, CreatedById = a.Me.Id, Email = a.Owner.Email, Subject = subject, Category = TicketCategories.All.Contains(i.Category) ? i.Category! : "other",
            OrderId = orderId, Status = TicketStatuses.Open, UnreadByStaff = true, CreatedAt = Now, UpdatedAt = Now,
        };
        t.Messages.Add(new TicketMessage { From = "client", By = a.Me.Name != "" ? a.Me.Name : a.Me.Email, Text = text, ImagesJson = ImagesJson(i.Images), At = Now });
        db.SupportTickets.Add(t);
        await db.SaveChangesAsync(ct);
        t.Number = $"T-{t.Id:D5}";
        await db.SaveChangesAsync(ct);
        await notifier.NotifyStaffAsync(NotificationTypes.TicketAdmin, subject, t.Id, ct);
        await realtime.ToStaffAsync("ticket", new { id = t.Id, number = t.Number, subject, status = t.Status });
        email.Enqueue(Templates.TeamTicket(t, text, site.Value.TeamEmail));
        return new SubmitResult(true, t.Id);
    }

    /// <summary>staff answer any ticket (from "oxira"); clients answer their account's tickets</summary>
    private async Task<(SupportTicket Ticket, bool AsStaff, Common.Account Account)> TicketAsync(int id, bool write, CancellationToken ct)
    {
        var a = write ? await access.WritableAsync(ct) : await access.AccountAsync(ct);
        var t = await db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var asStaff = a.IsStaff && a.IsOwner && t.OwnerId != a.Me.Id;
        if (!asStaff && t.OwnerId != a.Owner.Id) throw DomainException.NotFound();
        return (t, asStaff, a);
    }

    public async Task<Done> Handle(ReplyTicketCommand cmd, CancellationToken ct)
    {
        var (t, asStaff, a) = await TicketAsync(cmd.Input.Id ?? 0, true, ct);
        var text = Clip(cmd.Input.Message, 4000);
        if (text == "") throw new DomainException("اكتب ردك", "bad_request");
        db.TicketMessages.Add(new TicketMessage
        {
            TicketId = t.Id, From = asStaff ? "oxira" : "client", By = a.Me.Name != "" ? a.Me.Name : a.Me.Email, Text = text, ImagesJson = ImagesJson(cmd.Input.Images), At = Now,
        });
        t.UpdatedAt = Now;
        if (asStaff) { t.Status = TicketStatuses.Answered; t.UnreadByOwner = true; t.UnreadByStaff = false; }
        else { t.Status = TicketStatuses.Open; t.UnreadByStaff = true; }
        await db.SaveChangesAsync(ct);
        var payload = new { id = t.Id, number = t.Number, subject = t.Subject, status = t.Status, from = asStaff ? "oxira" : "client", text };
        if (asStaff)
        {
            await notifier.NotifyAsync(t.OwnerId, NotificationTypes.Ticket, subject: t.Subject, ticketId: t.Id, ct: ct);
            await realtime.ToUserAsync(t.OwnerId, "ticket", payload);
            var owner = await db.Users.AsNoTracking().FirstAsync(u => u.Id == t.OwnerId, ct);
            email.Enqueue(Templates.TicketReply(owner.Email, t, text, owner.Lang, site.Value));
        }
        else
        {
            await notifier.NotifyStaffAsync(NotificationTypes.TicketAdmin, t.Subject, t.Id, ct);
            await realtime.ToStaffAsync("ticket", payload);
            email.Enqueue(Templates.TeamTicket(t, text, site.Value.TeamEmail));
        }
        return new Done();
    }

    public async Task<Done> Handle(TicketActionCommand cmd, CancellationToken ct)
    {
        var (t, asStaff, a) = await TicketAsync(cmd.Id, cmd.Action != "read", ct);
        switch (cmd.Action)
        {
            case "read":
                if (asStaff) t.UnreadByStaff = false;
                else
                {
                    t.UnreadByOwner = false;
                    await db.Notifications.Where(n => n.UserId == t.OwnerId && n.TicketId == t.Id && !n.Read).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true), ct);
                }
                if (a.IsStaff) await db.Notifications.Where(n => n.UserId == a.Me.Id && n.TicketId == t.Id && !n.Read).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true), ct);
                break;
            case "close": t.Status = TicketStatuses.Closed; t.UpdatedAt = Now; break;
            case "reopen": t.Status = TicketStatuses.Open; t.UpdatedAt = Now; t.UnreadByStaff = !asStaff; break;
            default: throw new DomainException("عملية غير معروفة", "bad_request");
        }
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    // ───── team ─────
    public async Task<TeamResult> Handle(TeamQuery q, CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        if (!a.IsOwner) return new TeamResult(true, a.Role, []);
        var members = await (from t in db.TeamMembers.AsNoTracking()
                             join u in db.Users.AsNoTracking() on t.MemberUserId equals (Guid?)u.Id into users
                             from u in users.DefaultIfEmpty()
                             where t.OwnerId == a.Me.Id
                             orderby t.Id
                             select new TeamMemberDto(t.Id, u == null ? "" : u.Name, t.Email, t.Role, t.JoinedAt != null)).ToListAsync(ct);
        return new TeamResult(true, MemberRoles.Owner, members);
    }

    private async Task<User> OwnerOnlyAsync(CancellationToken ct)
    {
        var a = await access.AccountAsync(ct);
        if (!a.IsOwner) throw DomainException.Forbidden("صاحب الحساب فقط يدير الفريق", a.Role == MemberRoles.Viewer ? "viewer" : "forbidden");
        return a.Me;
    }

    public async Task<Done> Handle(InviteMemberCommand cmd, CancellationToken ct)
    {
        var me = await OwnerOnlyAsync(ct);
        var mail = Text.Email(cmd.Input.Email);
        if (!IsEmail(mail) || mail == me.Email) throw new DomainException("البريد الإلكتروني غير صحيح", "email");
        var role = MemberRoles.Valid(cmd.Input.Role) ? cmd.Input.Role! : MemberRoles.Viewer;
        var tm = await db.TeamMembers.FirstOrDefaultAsync(t => t.OwnerId == me.Id && t.Email == mail, ct);
        if (tm == null)
        {
            if (await db.TeamMembers.CountAsync(t => t.OwnerId == me.Id, ct) >= 20) throw DomainException.Limit("الحد الأقصى 20 عضوًا");
            tm = new TeamMember { OwnerId = me.Id, Email = mail, CreatedAt = Now };
            db.TeamMembers.Add(tm);
        }
        tm.Role = role;
        var invitee = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == mail && u.EmailVerified && !u.Suspended, ct);
        if (invitee != null && tm.MemberUserId == null) { tm.MemberUserId = invitee.Id; tm.JoinedAt = Now; }
        await db.SaveChangesAsync(ct);
        var lang = Langs.Of(cmd.Input.Lang ?? invitee?.Lang ?? me.Lang);
        email.Enqueue(Templates.TeamInvite(mail, me.Name, me.Email, role, site.Value.Account(lang) + (invitee == null ? "#signup" : ""), lang));
        return new Done();
    }

    public async Task<Done> Handle(SetMemberRoleCommand cmd, CancellationToken ct)
    {
        var me = await OwnerOnlyAsync(ct);
        if (!MemberRoles.Valid(cmd.Input.Role)) throw new DomainException("صلاحية غير معروفة", "bad_request");
        var tm = await db.TeamMembers.FirstOrDefaultAsync(t => t.Id == cmd.Input.Id && t.OwnerId == me.Id, ct) ?? throw DomainException.NotFound();
        tm.Role = cmd.Input.Role!;
        await db.SaveChangesAsync(ct);
        return new Done();
    }

    /// <summary>the member's switched sessions on this account end at once</summary>
    public async Task<Done> Handle(RemoveMemberCommand cmd, CancellationToken ct)
    {
        var me = await OwnerOnlyAsync(ct);
        var tm = await db.TeamMembers.FirstOrDefaultAsync(t => t.Id == cmd.Id && t.OwnerId == me.Id, ct) ?? throw DomainException.NotFound();
        db.TeamMembers.Remove(tm);
        await db.SaveChangesAsync(ct);
        if (tm.MemberUserId is { } member)
        {
            var now = Now;
            await db.RefreshTokens.Where(r => r.UserId == member && r.ActingOwnerId == me.Id && r.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, now).SetProperty(r => r.RevokedReason, "removed"), ct);
        }
        return new Done();
    }

    // ───── partner programme ─────
    public async Task<PartnerResult> Handle(PartnerQuery q, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        var p = await db.Partners.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == me.Id || x.Email == me.Email, ct);
        if (p == null) return new PartnerResult(true, null);
        var orders = await db.Orders.AsNoTracking().Where(o => o.Ref == p.Code).Select(o => new { o.PaidAt, o.Subtotal }).ToListAsync(ct);
        var earned = Math.Round(orders.Where(o => o.PaidAt != null).Sum(o => o.Subtotal) * p.Rate, 2);
        return new PartnerResult(true, new PartnerDto(p.Rate, p.Code, FormHandlers.PartnerLink(site.Value, p.Code), orders.Count, orders.Count(o => o.PaidAt != null),
            Math.Max(0, earned - p.PaidOut), p.PaidOut));
    }
}
