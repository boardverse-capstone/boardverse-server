using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using BoardVerse.Core.Enum;
using System.Reflection;

namespace BoardVerse.Data
{
    public class BoardVerseDbContext : DbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<TokenBlacklist> TokenBlacklists => Set<TokenBlacklist>();
        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
        public DbSet<Cafe> Cafes => Set<Cafe>();
        public DbSet<CafeStaff> CafeStaffs => Set<CafeStaff>();
        public DbSet<GameTemplate> GameTemplates => Set<GameTemplate>();
        public DbSet<GameComponentTemplate> GameComponentTemplates => Set<GameComponentTemplate>();

        public BoardVerseDbContext(DbContextOptions<BoardVerseDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply all IEntityTypeConfiguration<T> configurations from the assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            // User entity configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);

                entity.Property(u => u.Id)
                    .ValueGeneratedNever();

                entity.Property(u => u.Username)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(u => u.Email)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(u => u.PhoneNumber)
                    .HasMaxLength(50);

                entity.Property(u => u.PasswordHash)
                    .HasMaxLength(500);
                entity.Property(u => u.Role)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();
                entity.Property(u => u.Provider)
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasDefaultValue("Local");

                entity.Property(u => u.ProviderId)
                    .HasMaxLength(200);

                entity.Property(u => u.CreatedAt)
                    .IsRequired();

                entity.Property(u => u.UpdatedAt)
                    .IsRequired();

                entity.Property(u => u.IsEmailVerified)
                    .HasDefaultValue(false);

                entity.Property(u => u.IsActive)
                    .HasDefaultValue(true);

                entity.Property(u => u.IsBlocked)
                    .HasDefaultValue(false);

                entity.Property(u => u.BlockReason)
                    .HasMaxLength(500);

                entity.Property(u => u.BlockedAt);

                entity.HasIndex(u => u.Email)
                    .IsUnique();
            });

            // UserProfile entity configuration
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(p => p.UserId);

                entity.Property(p => p.UserId)
                    .ValueGeneratedNever();

                entity.Property(p => p.GamerTag)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(p => p.AvatarUrl)
                    .HasMaxLength(500);

                entity.Property(p => p.AvatarBorderUrl)
                    .HasMaxLength(500);

                entity.Property(p => p.Bio)
                    .HasMaxLength(1000);

                entity.Property(p => p.AvatarUrl)
                    .HasMaxLength(500);

                entity.Property(p => p.KarmaPoints)
                    .HasDefaultValue(100);

                entity.Property(p => p.GamerTier)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .HasDefaultValue(GamerTier.Bronze);

                entity.Property(p => p.GlobalElo)
                    .IsRequired();

                entity.Property(p => p.Level)
                    .IsRequired();

                entity.Property(p => p.CurrentExp)
                    .IsRequired();

                entity.Property(p => p.UpdatedAt)
                    .IsRequired();

                entity.Property(p => p.IsActive)
                    .IsRequired()
                    .HasDefaultValue(true);

                entity.HasOne(p => p.User)
                    .WithOne(u => u.Profile)
                    .HasForeignKey<UserProfile>(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(p => p.GamerTag)
                    .IsUnique();
            });

            // RefreshToken entity configuration
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(rt => rt.Id);

                entity.Property(rt => rt.Id)
                    .ValueGeneratedNever();

                entity.Property(rt => rt.Token)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(rt => rt.CreatedAt)
                    .IsRequired();

                entity.HasOne(rt => rt.User)
                    .WithMany()
                    .HasForeignKey(rt => rt.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(rt => rt.Token)
                    .IsUnique();

                entity.HasIndex(rt => rt.UserId);
            });

            // TokenBlacklist entity configuration
            modelBuilder.Entity<TokenBlacklist>(entity =>
            {
                entity.HasKey(tb => tb.Id);

                entity.Property(tb => tb.Id)
                    .ValueGeneratedNever();

                entity.Property(tb => tb.Token)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(tb => tb.CreatedAt)
                    .IsRequired();

                entity.HasOne(tb => tb.User)
                    .WithMany()
                    .HasForeignKey(tb => tb.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(tb => tb.Token);

                entity.HasIndex(tb => tb.UserId);
            });

            // PasswordResetToken entity configuration
            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasKey(prt => prt.Id);

                entity.Property(prt => prt.Id)
                    .ValueGeneratedNever();

                entity.Property(prt => prt.Token)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(prt => prt.CreatedAt)
                    .IsRequired();

                entity.HasOne(prt => prt.User)
                    .WithMany()
                    .HasForeignKey(prt => prt.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(prt => prt.Token);

                entity.HasIndex(prt => prt.UserId);
            });

            // Cafe entity configuration
            modelBuilder.Entity<Cafe>(entity =>
            {
                entity.HasKey(c => c.Id);

                entity.Property(c => c.Id)
                    .ValueGeneratedNever();

                entity.Property(c => c.Name)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(c => c.Address)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(c => c.PhoneNumber)
                    .HasMaxLength(50);

                entity.Property(c => c.Description)
                    .HasMaxLength(1000);

                entity.Property(c => c.CreatedAt)
                    .IsRequired();

                entity.Property(c => c.IsActive)
                    .IsRequired()
                    .HasDefaultValue(true);

                entity.HasOne(c => c.Manager)
                    .WithMany()
                    .HasForeignKey(c => c.ManagerId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(c => c.ManagerId);
            });

            // CafeStaff junction entity configuration
            modelBuilder.Entity<CafeStaff>(entity =>
            {
                entity.HasKey(cs => new { cs.CafeId, cs.UserId });

                entity.Property(cs => cs.JoinedAt)
                    .IsRequired();

                entity.Property(cs => cs.IsActive)
                    .IsRequired()
                    .HasDefaultValue(true);

                entity.HasOne(cs => cs.Cafe)
                    .WithMany(c => c.StaffMembers)
                    .HasForeignKey(cs => cs.CafeId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(cs => cs.User)
                    .WithMany()
                    .HasForeignKey(cs => cs.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Note: GameTemplate and GameComponentTemplate configurations are now handled
            // by IEntityTypeConfiguration<T> classes in the Configurations folder
            // and are automatically applied via ApplyConfigurationsFromAssembly
        }
    }
}
