using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Infrastructure.Data;

public partial class OxiraDbContext
{
    public DbSet<SiteBooking> SiteBookings => Set<SiteBooking>();
    public DbSet<EventTicket> EventTickets => Set<EventTicket>();
    public DbSet<SiteReview> SiteReviews => Set<SiteReview>();
    public DbSet<SiteMessage> SiteMessages => Set<SiteMessage>();
    public DbSet<ContentPack> ContentPacks => Set<ContentPack>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();

    partial void ConfigurePortal(ModelBuilder b)
    {
        b.Entity<SiteBooking>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Service).HasMaxLength(160);
            e.Property(x => x.Date).HasMaxLength(10);
            e.Property(x => x.Time).HasMaxLength(5);
            e.Property(x => x.Note).HasMaxLength(600);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.HasIndex(x => new { x.OrderId, x.Date, x.Time });
        });
        b.Entity<EventTicket>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(16);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Type).HasMaxLength(40);
            e.Property(x => x.TypeName).HasMaxLength(80);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.HasIndex(x => new { x.OrderId, x.Type, x.Status });
        });
        b.Entity<SiteReview>(e =>
        {
            e.Property(x => x.Comment).HasMaxLength(1500);
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasIndex(x => x.OrderId);
        });
        b.Entity<SiteMessage>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.OrderId);
        });
        b.Entity<ContentPack>(e =>
        {
            e.Property(x => x.Month).HasMaxLength(7);
            e.HasIndex(x => new { x.OrderId, x.Month }).IsUnique();
        });
        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(16);
            e.Property(x => x.Product).HasMaxLength(20);
            e.Property(x => x.Site).HasMaxLength(253);
            e.Property(x => x.Date).HasMaxLength(20);
            e.Property(x => x.Subject).HasMaxLength(140);
            e.Property(x => x.Href).HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.Read, x.Id });
        });
        b.Entity<SupportTicket>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(12);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Subject).HasMaxLength(140);
            e.Property(x => x.Category).HasMaxLength(12);
            e.Property(x => x.Status).HasMaxLength(10);
            e.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TicketMessage>(e =>
        {
            e.Property(x => x.From).HasMaxLength(8);
            e.Property(x => x.By).HasMaxLength(160);
            e.Property(x => x.ImagesJson).HasMaxLength(400);
        });
        ConfigureGrowth(b);
    }

    partial void ConfigureGrowth(ModelBuilder b);
}
