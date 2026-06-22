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
        public DbSet<Cafe> Cafes => Set<Cafe>();
        public DbSet<CafeStaff> CafeStaffs => Set<CafeStaff>();
        public DbSet<CafePartnerApplication> CafePartnerApplications => Set<CafePartnerApplication>();
        public DbSet<GameTemplate> GameTemplates => Set<GameTemplate>();
        public DbSet<GameComponentTemplate> GameComponentTemplates => Set<GameComponentTemplate>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<GameTemplateCategory> GameTemplateCategories => Set<GameTemplateCategory>();
        public DbSet<CafeGameInventory> CafeGameInventories => Set<CafeGameInventory>();
        public DbSet<CafeGameComponentPenalty> CafeGameComponentPenalties => Set<CafeGameComponentPenalty>();
        public DbSet<PlayerLocationHistory> PlayerLocationHistories => Set<PlayerLocationHistory>();
        public DbSet<CafeTable> CafeTables => Set<CafeTable>();
        public DbSet<CafeInventoryBox> CafeInventoryBoxes => Set<CafeInventoryBox>();
        public DbSet<ActiveSession> ActiveSessions => Set<ActiveSession>();
        public DbSet<Lobby> Lobbies => Set<Lobby>();
        public DbSet<LobbyMember> LobbyMembers => Set<LobbyMember>();
        public DbSet<PlayerKarmaRating> PlayerKarmaRatings => Set<PlayerKarmaRating>();
        public DbSet<MatchResult> MatchResults => Set<MatchResult>();
        public DbSet<MatchHistory> MatchHistories => Set<MatchHistory>();
        public DbSet<MatchHistoryParticipant> MatchHistoryParticipants => Set<MatchHistoryParticipant>();
        public DbSet<KarmaLog> KarmaLogs => Set<KarmaLog>();
        public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();

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

                entity.Property(u => u.BlockReason)
                    .HasMaxLength(500);

                entity.Property(u => u.BlockedAt);

                entity.Property(u => u.AccountStatus)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .HasDefaultValue(UserAccountStatus.Active);

                entity.Property(u => u.LockoutEndDate);

                entity.HasIndex(u => u.Email)
                    .IsUnique();
            });

            // UserProfile entity configuration
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(p => p.UserId);

                entity.Property(p => p.UserId)
                    .ValueGeneratedNever();

                entity.Property(p => p.AvatarUrl)
                    .HasMaxLength(500);

                entity.Property(p => p.AvatarBorderUrl)
                    .HasMaxLength(500);

                entity.Property(p => p.Bio)
                    .HasMaxLength(1000);

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

                entity.Property(p => p.DateOfBirth)
                    .HasColumnType("date");

                entity.Property(p => p.LastKnownLatitude)
                    .HasColumnType("double precision");

                entity.Property(p => p.LastKnownLongitude)
                    .HasColumnType("double precision");

                entity.Property(p => p.LastLocationSource)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.HasOne(p => p.User)
                    .WithOne(u => u.Profile)
                    .HasForeignKey<UserProfile>(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

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

            // Cafe entity configuration is in Configurations/CafeConfiguration.cs

            modelBuilder.Entity<CafePartnerApplication>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Id).ValueGeneratedNever();
                entity.Property(a => a.CafeName).IsRequired().HasMaxLength(100);
                entity.Property(a => a.Address).IsRequired().HasMaxLength(500);
                entity.Property(a => a.Latitude).HasColumnType("double precision");
                entity.Property(a => a.Longitude).HasColumnType("double precision");
                entity.Property(a => a.Hotline).IsRequired().HasMaxLength(11);
                entity.Property(a => a.RepresentativeEmail).IsRequired().HasMaxLength(256);
                entity.Property(a => a.BusinessLicense).IsRequired().HasMaxLength(50);
                entity.Property(a => a.BusinessLicenseImageUrl).HasMaxLength(500);
                entity.Property(a => a.SpaceImageUrlsJson).IsRequired().HasDefaultValue("[]");
                entity.Property(a => a.TableLayoutJson).IsRequired().HasDefaultValue("[]");
                entity.Property(a => a.PopularGamesList).IsRequired().HasMaxLength(2000);
                entity.Property(a => a.BillingModel)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();
                entity.Property(a => a.Status)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();
                entity.Property(a => a.RejectionReason).HasMaxLength(1000);
                entity.Property(a => a.SubmittedAt).IsRequired();
                entity.Property(a => a.UpdatedAt).IsRequired();
                entity.HasIndex(a => a.RepresentativeEmail);
                entity.HasIndex(a => a.BusinessLicense);
                entity.HasIndex(a => a.Hotline);
                entity.HasIndex(a => a.Status);
                entity.HasIndex(a => a.SubmittedByUserId);

                entity.HasOne(a => a.SubmittedByUser)
                    .WithMany()
                    .HasForeignKey(a => a.SubmittedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(a => a.ReviewedByAdmin)
                    .WithMany()
                    .HasForeignKey(a => a.ReviewedByAdminId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(a => a.CreatedManager)
                    .WithMany()
                    .HasForeignKey(a => a.CreatedManagerUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(a => a.CreatedCafe)
                    .WithMany()
                    .HasForeignKey(a => a.CreatedCafeId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // CafeStaff junction entity configuration
            modelBuilder.Entity<CafeStaff>(entity =>
            {
                entity.HasKey(cs => new { cs.CafeId, cs.UserId });

                entity.Property(cs => cs.JoinedAt)
                    .IsRequired();

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
