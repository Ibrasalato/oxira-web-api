using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Oxira.Web.Application.Common;
using Oxira.Web.Domain;

namespace Oxira.Web.Infrastructure.Data;

public partial class OxiraDbContext(DbContextOptions<OxiraDbContext> options) : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailCode> EmailCodes => Set<EmailCode>();
    public DbSet<MagicLink> MagicLinks => Set<MagicLink>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    /// <summary>ASP.NET Data Protection keys (encrypts stored WordPress and Meta secrets), shared by every server</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(160);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Company).HasMaxLength(120);
            e.Property(x => x.AvatarId).HasMaxLength(48);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(12);
            e.Property(x => x.SecurityStamp).HasMaxLength(40);
            e.Property(x => x.Lang).HasMaxLength(5);
        });
        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasIndex(x => x.FamilyId);
            e.Property(x => x.RevokedReason).HasMaxLength(20);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
        });
        b.Entity<EmailCode>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Purpose).HasMaxLength(12);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => new { x.Email, x.Purpose, x.CreatedAt });
        });
        b.Entity<MagicLink>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.HasIndex(x => new { x.Email, x.CreatedAt });
            e.Property(x => x.ReturnUrl).HasMaxLength(1000);
        });
        b.Entity<TeamMember>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Role).HasMaxLength(10);
            e.HasIndex(x => new { x.OwnerId, x.Email }).IsUnique();
            e.HasIndex(x => x.MemberUserId);
        });
        b.Entity<UsageCounter>(e =>
        {
            e.HasKey(x => new { x.Key, x.Day });
            e.Property(x => x.Key).HasMaxLength(120);
        });
        ConfigureBusiness(b);
    }

    partial void ConfigureBusiness(ModelBuilder b);

    // ───── transactions with application locks ─────
    public async Task<IUnitOfWork> BeginAsync(IEnumerable<string> locks, CancellationToken ct = default)
    {
        var owns = Database.CurrentTransaction is null;
        var tx = owns ? await Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct) : Database.CurrentTransaction!;
        // sorted, so two operations taking the same locks can never deadlock
        foreach (var key in locks.Distinct().OrderBy(k => k, StringComparer.Ordinal))
        {
            var resource = key.Length > 200 ? key[..200] : key;
            await Database.ExecuteSqlInterpolatedAsync(
                $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 50001, 'busy', 1;", ct);
        }
        return new UnitOfWork(this, owns ? tx : null);
    }

    private sealed class UnitOfWork(OxiraDbContext db, IDbContextTransaction? tx) : IUnitOfWork
    {
        private bool _done;
        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (tx == null) return; // joined an outer transaction: the outer one commits
            await tx.CommitAsync(ct);
            _done = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (tx == null) return;
            if (!_done)
            {
                await tx.RollbackAsync();
                db.ChangeTracker.Clear();
            }
            await tx.DisposeAsync();
        }
    }
}

/// <summary>Lets `dotnet ef` build the model without starting the API.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<OxiraDbContext>
{
    public OxiraDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OxiraDbContext>()
        .UseSqlServer("Server=localhost;Database=OxiraWeb;Trusted_Connection=True;TrustServerCertificate=True").Options);
}

/// <summary>Applies migrations at startup (or creates the schema when the project has none yet), then seeds.</summary>
public class DbInitializer(OxiraDbContext db, ILogger<DbInitializer> log, IEnumerable<IDbSeed> seeds)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync(ct);
        else await db.Database.EnsureCreatedAsync(ct);
        foreach (var s in seeds) await s.SeedAsync(ct);
        log.LogInformation("Database ready");
    }
}

/// <summary>Start-up data (example demos, settings) added once.</summary>
public interface IDbSeed
{
    Task SeedAsync(CancellationToken ct);
}
