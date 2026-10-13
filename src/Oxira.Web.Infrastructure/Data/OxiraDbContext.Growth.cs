using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Infrastructure.Data;

public partial class OxiraDbContext
{
    public DbSet<SeoItem> SeoItems => Set<SeoItem>();
    public DbSet<SeoProfile> SeoProfiles => Set<SeoProfile>();
    public DbSet<SocialProfile> SocialProfiles => Set<SocialProfile>();
    public DbSet<SocialItem> SocialItems => Set<SocialItem>();
    public DbSet<StatusCheck> StatusChecks => Set<StatusCheck>();

    partial void ConfigureGrowth(ModelBuilder b)
    {
        b.Entity<SeoItem>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(90);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Url).HasMaxLength(400);
            e.HasIndex(x => new { x.OrderId, x.Status });
        });
        b.Entity<SeoProfile>(e =>
        {
            e.HasKey(x => x.OrderId);
            e.Property(x => x.OrderId).ValueGeneratedNever();
            e.Property(x => x.WpUrl).HasMaxLength(200);
            e.Property(x => x.WpUser).HasMaxLength(80);
        });
        b.Entity<SocialProfile>(e =>
        {
            e.HasKey(x => x.OrderId);
            e.Property(x => x.OrderId).ValueGeneratedNever();
            e.Property(x => x.LastPlanMonth).HasMaxLength(7);
        });
        b.Entity<SocialItem>(e =>
        {
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.Kind).HasMaxLength(8);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Hashtags).HasMaxLength(400);
            e.Property(x => x.Day).HasMaxLength(10);
            e.Property(x => x.Time).HasMaxLength(5);
            e.Property(x => x.Platforms).HasMaxLength(40);
            e.Property(x => x.Bg).HasMaxLength(48);
            e.Property(x => x.Image).HasMaxLength(48);
            e.HasIndex(x => new { x.OrderId, x.Month });
            e.HasIndex(x => new { x.Status, x.Day });
        });
        b.Entity<StatusCheck>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(20);
            e.HasIndex(x => new { x.Key, x.At });
            e.HasIndex(x => x.At);
        });
    }
}
