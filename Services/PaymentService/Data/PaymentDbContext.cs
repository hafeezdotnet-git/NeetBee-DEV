using Microsoft.EntityFrameworkCore;
using PaymentService.Entities;

namespace PaymentService.Data;

/// <summary>
/// Maps dbo.PaymentTransactions (Database/008_create_payment_transactions.sql) and a read-only view of dbo.Users.
/// The schema is owned by the scripts in Database/; EF Core does not create or migrate it.
/// </summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    /// <summary>Read only: used to find the payer's name and email. AuthenticationService owns this table.</summary>
    public IQueryable<PaymentUser> Users => Set<PaymentUser>().AsNoTracking();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentUser>(e =>
        {
            e.ToTable("Users", t => t.ExcludeFromMigrations());
            e.HasKey(u => u.Id);
            e.Property(u => u.UserName).HasMaxLength(64);
            e.Property(u => u.Email).HasMaxLength(256);
        });

        modelBuilder.Entity<PaymentTransaction>(e =>
        {
            e.ToTable("PaymentTransactions");
            e.HasKey(t => t.Id);
            e.Property(t => t.Amount).HasColumnType("decimal(12,2)");
            e.Property(t => t.Currency).HasColumnType("char(3)").IsRequired();
            e.Property(t => t.Method).HasMaxLength(20).IsUnicode(false).IsRequired();
            e.Property(t => t.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
            e.Property(t => t.Description).HasMaxLength(200);
            e.Property(t => t.MethodDetail).HasMaxLength(64);
            e.Property(t => t.Gateway).HasMaxLength(32).IsUnicode(false).IsRequired();
            e.Property(t => t.GatewayReference).HasMaxLength(100).IsUnicode(false);
            e.Property(t => t.FailureReason).HasMaxLength(400);
            e.Property(t => t.CreatedAt).HasColumnType("datetime2(7)");
            e.Property(t => t.UpdatedAt).HasColumnType("datetime2(7)");
            e.Property(t => t.CompletedAt).HasColumnType("datetime2(7)");
            e.HasIndex(t => new { t.UserId, t.CreatedAt });
            e.HasOne<PaymentUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId);
        });
    }
}
