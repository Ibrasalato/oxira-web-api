using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Api.Infrastructure;

/// <summary>
/// /hubs/account: the signed-in client (access token in ?access_token=). Server → client events:
/// "notification" (a new bell item), "ticket" (a support reply), "payment" (an order or subscription was paid).
/// Oxira team members also join the "staff" group (new tickets, orders).
/// </summary>
[Authorize]
public class AccountHub(IAppDbContext db, ILogger<AccountHub> log) : Hub
{
    public const string Staff = "staff";

    public override async Task OnConnectedAsync()
    {
        // a token issued before a password change, role change or suspension must not keep a live channel open
        var stamp = Context.User?.FindFirstValue(OxiraClaims.Stamp);
        var u = Guid.TryParse(Context.UserIdentifier, out var id)
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Suspended && x.SecurityStamp == stamp, Context.ConnectionAborted)
            : null;
        if (u == null)
        {
            log.LogInformation("Hub connection refused for {User}: stale session", Context.UserIdentifier);
            Context.Abort();
            return;
        }
        if (Roles.IsStaff(u.Role)) await Groups.AddToGroupAsync(Context.ConnectionId, Staff);
        await base.OnConnectedAsync();
    }
}

/// <summary>/hubs/orders: the studio and bundle pages wait here for "payment" after returning from Moyasar (no polling).</summary>
public class OrdersHub : Hub
{
    public static string Group(int orderId) => $"order:{orderId}";
    public Task WatchOrder(int orderId) => Groups.AddToGroupAsync(Context.ConnectionId, Group(orderId));
    public Task UnwatchOrder(int orderId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(orderId));
}

/// <summary>SignalR addresses users by the JWT subject (the user id).</summary>
public class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext c) => c.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);
}

public class SignalRRealtime(IHubContext<AccountHub> account, IHubContext<OrdersHub> orders, ILogger<SignalRRealtime> log) : IRealtime
{
    public Task ToUserAsync(Guid userId, string evt, object payload) => Safe(evt, () => account.Clients.User(userId.ToString()).SendAsync(evt, payload));
    public Task ToStaffAsync(string evt, object payload) => Safe(evt, () => account.Clients.Group(AccountHub.Staff).SendAsync(evt, payload));
    public Task ToOrderAsync(int orderId, string evt, object payload) => Safe(evt, () => orders.Clients.Group(OrdersHub.Group(orderId)).SendAsync(evt, payload));

    private async Task Safe(string evt, Func<Task> send)
    {
        try { await send(); }
        catch (Exception e) { log.LogWarning(e, "Realtime {Event} failed", evt); }
    }
}
