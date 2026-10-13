using Microsoft.EntityFrameworkCore;
using Oxira.Web.Domain;

namespace Oxira.Web.Infrastructure.Data;

public partial class OxiraDbContext
{
    public DbSet<Upload> Uploads => Set<Upload>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    partial void ConfigureCommerce(ModelBuilder b)
    {
        b.Entity<Upload>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(48);
            e.Property(x => x.Mime).HasMaxLength(30);
            e.Property(x => x.SessionId).HasMaxLength(64);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.CreatedAt);
        });
        b.Entity<Draft>(e => e.Property(x => x.Id).HasMaxLength(48));
        b.Entity<Order>(e =>
        {
            // public order numbers start at 1001
            e.Property(x => x.Id).UseIdentityColumn(1001, 1);
            e.Property(x => x.Kind).HasMaxLength(12);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.ClientType).HasMaxLength(40);
            e.Property(x => x.Template).HasMaxLength(60);
            e.Property(x => x.SiteName).HasMaxLength(120);
            e.Property(x => x.SiteLang).HasMaxLength(5);
            e.Property(x => x.DomainOption).HasMaxLength(8);
            e.Property(x => x.DomainName).HasMaxLength(253);
            e.Property(x => x.Addons).HasMaxLength(200);
            e.Property(x => x.Plan).HasMaxLength(20);
            e.Property(x => x.Subtotal).HasPrecision(12, 2);
            e.Property(x => x.Vat).HasPrecision(12, 2);
            e.Property(x => x.Total).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Url).HasMaxLength(300);
            e.Property(x => x.Slug).HasMaxLength(63);
            e.Property(x => x.VerifyToken).HasMaxLength(64);
            e.Property(x => x.Ref).HasMaxLength(20);
            e.Property(x => x.Lang).HasMaxLength(5);
            e.Property(x => x.Page).HasMaxLength(300);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.Slug);
            e.HasIndex(x => x.Ref);
        });
        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(14);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Subtotal).HasPrecision(12, 2);
            e.Property(x => x.Vat).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Provider).HasMaxLength(12);
            e.Property(x => x.ProviderId).HasMaxLength(80);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(12);
            e.HasIndex(x => x.ProviderId);
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.SubscriptionId);
        });
        b.Entity<Invoice>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.PaymentId).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Buyer).HasMaxLength(120);
            e.Property(x => x.Product).HasMaxLength(20);
            e.Property(x => x.Kind).HasMaxLength(14);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Site).HasMaxLength(253);
            e.Property(x => x.Subtotal).HasPrecision(12, 2);
            e.Property(x => x.Vat).HasPrecision(12, 2);
            e.Property(x => x.Total).HasPrecision(12, 2);
        });
        b.Entity<Subscription>(e =>
        {
            e.Property(x => x.Product).HasMaxLength(20);
            e.Property(x => x.Price).HasPrecision(12, 2);
            e.Property(x => x.Period).HasMaxLength(6);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.InvoiceUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.OrderId, x.Product });
            e.HasIndex(x => new { x.Status, x.NextDue });
        });
        ConfigurePortal(b);
    }

    partial void ConfigurePortal(ModelBuilder b);
}
