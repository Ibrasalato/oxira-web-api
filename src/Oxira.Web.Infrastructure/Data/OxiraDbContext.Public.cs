using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oxira.Web.Application.Features.Agent;
using Oxira.Web.Domain;

namespace Oxira.Web.Infrastructure.Data;

public partial class OxiraDbContext
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<Testimonial> Testimonials => Set<Testimonial>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AuditRun> AuditRuns => Set<AuditRun>();
    public DbSet<AgentDemo> AgentDemos => Set<AgentDemo>();
    public DbSet<AgentSubscription> AgentSubscriptions => Set<AgentSubscription>();

    partial void ConfigureBusiness(ModelBuilder b)
    {
        b.Entity<Lead>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(12);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Company).HasMaxLength(160);
            e.Property(x => x.Service).HasMaxLength(160);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Page).HasMaxLength(300);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(12);
            e.HasIndex(x => new { x.Kind, x.CreatedAt });
        });
        b.Entity<Partner>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Company).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Email).HasMaxLength(160);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Code).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Rate).HasPrecision(5, 4);
            e.Property(x => x.PaidOut).HasPrecision(12, 2);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.HasIndex(x => x.UserId);
        });
        b.Entity<Testimonial>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Role).HasMaxLength(80);
            e.Property(x => x.Company).HasMaxLength(100);
            e.Property(x => x.Text).HasMaxLength(800);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Status).HasMaxLength(12);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
        b.Entity<ChatMessage>(e =>
        {
            e.Property(x => x.SessionId).HasMaxLength(64);
            e.Property(x => x.Channel).HasMaxLength(20);
            e.Property(x => x.Role).HasMaxLength(12);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Page).HasMaxLength(300);
            e.HasIndex(x => new { x.Channel, x.SessionId, x.Id });
        });
        b.Entity<AuditRun>(e =>
        {
            e.Property(x => x.Url).HasMaxLength(300);
            e.Property(x => x.Host).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.CreatedAt);
        });
        b.Entity<AgentDemo>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(16);
            e.Property(x => x.Business).HasMaxLength(100);
            e.Property(x => x.Url).HasMaxLength(300);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Notes).HasMaxLength(1500);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Welcome).HasMaxLength(1000);
            e.Property(x => x.SuggestionsJson).HasMaxLength(1000);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.CreatedAt);
        });
        b.Entity<AgentSubscription>(e =>
        {
            e.Property(x => x.DemoId).HasMaxLength(16);
            e.Property(x => x.Plan).HasMaxLength(10);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Business).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Whatsapp).HasMaxLength(40);
            e.Property(x => x.City).HasMaxLength(60);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Status).HasMaxLength(12);
        });
        ConfigureCommerce(b);
    }

    partial void ConfigureCommerce(ModelBuilder b);
}

/// <summary>The fictional example businesses linked from /whatsapp-agent/ (their ids are fixed in the website).</summary>
public class ExampleDemosSeed(OxiraDbContext db, TimeProvider clock, ILogger<ExampleDemosSeed> log) : IDbSeed
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var have = await db.AgentDemos.Where(d => d.Example).Select(d => d.Id).ToListAsync(ct);
        var added = 0;
        foreach (var x in AgentExamples.All.Where(x => !have.Contains(x.Id)))
        {
            db.AgentDemos.Add(new AgentDemo
            {
                Id = x.Id, Business = x.Business, Lang = x.Lang, Profile = x.Profile, Welcome = x.Welcome,
                SuggestionsJson = JsonSerializer.Serialize(x.Suggestions), Example = true, CreatedAt = clock.GetUtcNow(),
            });
            added++;
        }
        if (added > 0) { await db.SaveChangesAsync(ct); log.LogInformation("Seeded {Count} example agent demos", added); }
    }
}
