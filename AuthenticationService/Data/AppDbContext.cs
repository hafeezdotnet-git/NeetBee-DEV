using AuthenticationService.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthenticationService.Data;

/// <summary>
/// Maps to the tables created by Database/001_create_neet_bee.sql.
/// The schema is owned by that script; EF Core does not create or migrate it.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<EmailOtpCode> EmailOtpCodes => Set<EmailOtpCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(u => u.Id);
            e.Property(u => u.UserName).HasMaxLength(64).IsRequired();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired(false); // null for Gmail-only accounts
            e.Property(u => u.CreatedAt).HasColumnType("datetime2(7)");
            e.HasIndex(u => u.UserName).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<EmailOtpCode>(e =>
        {
            e.ToTable("EmailOtpCodes");
            e.HasKey(c => c.Id);
            e.Property(c => c.Email).HasMaxLength(256).IsRequired();
            e.Property(c => c.CodeHash).HasColumnType("char(64)").IsRequired();
            e.Property(c => c.CreatedAt).HasColumnType("datetime2(7)");
            e.Property(c => c.ExpiresAt).HasColumnType("datetime2(7)");
            e.Property(c => c.ConsumedAt).HasColumnType("datetime2(7)");
            e.HasIndex(c => new { c.Email, c.CreatedAt });
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(r => r.Id);
            e.Property(r => r.TokenHash).HasColumnType("char(64)").IsRequired();
            e.Property(r => r.ReplacedByTokenHash).HasColumnType("char(64)");
            e.Property(r => r.CreatedAt).HasColumnType("datetime2(7)");
            e.Property(r => r.ExpiresAt).HasColumnType("datetime2(7)");
            e.Property(r => r.RevokedAt).HasColumnType("datetime2(7)");
            e.Property(r => r.CreatedByIp).HasMaxLength(45).IsUnicode(false);
            e.Property(r => r.RevokedByIp).HasMaxLength(45).IsUnicode(false);
            e.HasIndex(r => r.TokenHash).IsUnique();
            e.HasIndex(r => r.FamilyId);
            e.HasIndex(r => r.UserId);
            e.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
