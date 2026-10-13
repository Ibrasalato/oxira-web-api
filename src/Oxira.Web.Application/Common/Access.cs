using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Application.Common;

/// <summary>
/// The account a request works on. Normally the signed-in user's own (role owner); after an account switch,
/// the team owner's account with the member's role (full or viewer).
/// </summary>
public record Account(User Me, User Owner, string Role)
{
    public bool IsOwner => Role == MemberRoles.Owner;
    public bool CanWrite => Role != MemberRoles.Viewer;
    public bool IsStaff => Roles.IsStaff(Me.Role);
}

/// <summary>Who is calling, re-checked against the database on every request (a suspended account stops at once).</summary>
public class Access(IAppDbContext db, ICurrentUser current)
{
    private User? _me;
    private Account? _account;

    public async Task<User> MeAsync(CancellationToken ct)
    {
        if (_me != null) return _me;
        if (current.Id is not { } id) throw DomainException.Session();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.Session();
        // password changed, role changed or account suspended since this token was issued
        if (u.Suspended || current.Stamp != u.SecurityStamp) throw DomainException.Session();
        return _me = u;
    }

    public async Task<User?> MaybeAsync(CancellationToken ct)
    {
        if (current.Id is null) return null;
        try { return await MeAsync(ct); } catch (DomainException) { return null; }
    }

    public async Task<Account> AccountAsync(CancellationToken ct)
    {
        if (_account != null) return _account;
        var me = await MeAsync(ct);
        if (current.ActingOwnerId is { } ownerId && ownerId != me.Id)
        {
            var tm = await db.TeamMembers.AsNoTracking().FirstOrDefaultAsync(t => t.OwnerId == ownerId && t.MemberUserId == me.Id && t.JoinedAt != null, ct)
                ?? throw DomainException.Session("لم تعد عضوًا في هذا الحساب");
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == ownerId && !u.Suspended, ct) ?? throw DomainException.Session();
            return _account = new Account(me, owner, tm.Role);
        }
        return _account = new Account(me, me, MemberRoles.Owner);
    }

    /// <summary>the account, for a change: viewers are read-only</summary>
    public async Task<Account> WritableAsync(CancellationToken ct)
    {
        var a = await AccountAsync(ct);
        if (!a.CanWrite) throw DomainException.Forbidden("صلاحيتك على هذا الحساب للاطلاع فقط", "viewer");
        return a;
    }

    public async Task<User> StaffAsync(CancellationToken ct)
    {
        var u = await MeAsync(ct);
        if (!Roles.IsStaff(u.Role)) throw DomainException.Forbidden("هذه العملية لفريق أوكسيرا فقط");
        return u;
    }
}
