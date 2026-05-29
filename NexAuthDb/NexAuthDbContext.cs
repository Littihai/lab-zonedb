// ============================================================
//  NexAuth — Infrastructure: EF Core DbContext
//  Exact mapping to NexAuthDb schema
// ============================================================
using Microsoft.EntityFrameworkCore;
using NexAuth.Domain.Entities;

namespace NexAuth.Infrastructure.Persistence
{
    public class NexAuthDbContext : DbContext
    {
        public NexAuthDbContext(DbContextOptions<NexAuthDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
        public DbSet<LoginAuditLog> LoginAuditLogs => Set<LoginAuditLog>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // ── Users ────────────────────────────────────────
            mb.Entity<User>(e =>
            {
                e.ToTable("Users");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()"); 
                e.Property(x => x.Email).HasMaxLength(256).IsRequired();
                e.HasIndex(x => x.Email).IsUnique();
                e.Property(x => x.FullName).HasMaxLength(256).IsRequired();
                e.Property(x => x.PasswordHash).HasMaxLength(512);
                e.Property(x => x.AvatarUrl).HasMaxLength(1024);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");   
                e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");     
            });

            // ── Roles ────────────────────────────────────────
            mb.Entity<Role>(e =>
            {
                e.ToTable("Roles");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.Property(x => x.Description).HasMaxLength(500);

                // Seed default role
                e.HasData(new Role { Id = new Guid("00000000-0000-0000-0000-000000000001"), Name = "User", Description = "Standard user" });
                e.HasData(new Role { Id = new Guid("00000000-0000-0000-0000-000000000002"), Name = "Admin", Description = "Administrator" });
            });

            // ── UserRoles (composite PK) ──────────────────────
            mb.Entity<UserRole>(e =>
            {
                e.ToTable("UserRoles");
                e.HasKey(x => new { x.UserId, x.RoleId });
                e.Property(x => x.AssignedAt).HasDefaultValueSql("now()");

                e.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            });

            // ── ExternalLogins ────────────────────────────────
            mb.Entity<ExternalLogin>(e =>
            {
                e.ToTable("ExternalLogins");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
                e.Property(x => x.ProviderKey).HasMaxLength(256).IsRequired();
                e.Property(x => x.ProviderDisplayName).HasMaxLength(256);
                e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
                e.HasIndex(x => new { x.Provider, x.ProviderKey }).IsUnique();

                e.HasOne(x => x.User).WithMany(u => u.ExternalLogins).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            // ── RefreshTokens ─────────────────────────────────
            mb.Entity<RefreshToken>(e =>
            {
                e.ToTable("RefreshTokens");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Token).HasMaxLength(512).IsRequired();
                e.HasIndex(x => x.Token).IsUnique();
                e.Property(x => x.IsRevoked).HasDefaultValue(false);
                e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
                e.Property(x => x.ReplacedByToken).HasMaxLength(512);

                e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            // ── PasswordResets ────────────────────────────────
            mb.Entity<PasswordReset>(e =>
            {
                e.ToTable("PasswordResets");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Token).HasMaxLength(512).IsRequired();
                e.HasIndex(x => x.Token).IsUnique();
                e.Property(x => x.IsUsed).HasDefaultValue(false);
                e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

                e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            // ── LoginAuditLogs ────────────────────────────────
            mb.Entity<LoginAuditLog>(e =>
            {
                e.ToTable("LoginAuditLogs");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Method).HasMaxLength(20).IsRequired();
                e.Property(x => x.Email).HasMaxLength(256);
                e.Property(x => x.IpAddress).HasMaxLength(45);
                e.Property(x => x.UserAgent).HasMaxLength(512);
                e.Property(x => x.FailReason).HasMaxLength(256);
                e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

                // No FK — logs must survive user deletion
            });

            // ── Ignore computed properties ────────────────────
            mb.Entity<RefreshToken>().Ignore(x => x.IsExpired).Ignore(x => x.IsActive);
            mb.Entity<PasswordReset>().Ignore(x => x.IsExpired).Ignore(x => x.IsValid);
        }
    }
}
