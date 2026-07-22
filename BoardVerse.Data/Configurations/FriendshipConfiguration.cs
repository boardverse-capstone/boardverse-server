using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("Friendships");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Message).HasMaxLength(200);

        builder.Property(f => f.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.CreatedAt).IsRequired();
        builder.Property(f => f.UpdatedAt).IsRequired();

        builder.HasOne(f => f.Requester)
            .WithMany()
            .HasForeignKey(f => f.RequesterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Addressee)
            .WithMany()
            .HasForeignKey(f => f.AddresseeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique (Requester, Addressee) - BR-FRIEND-01
        builder.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
        builder.HasIndex(f => f.AddresseeId);
        builder.HasIndex(f => new { f.Status, f.CreatedAt });

        // Filter cho auto-expire job
        builder.HasIndex(f => f.CreatedAt)
            .HasFilter("\"Status\" = 'Pending'");
    }
}

public class FriendNoteConfiguration : IEntityTypeConfiguration<FriendNote>
{
    public void Configure(EntityTypeBuilder<FriendNote> builder)
    {
        builder.ToTable("FriendNotes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.Alias).IsRequired().HasMaxLength(100);
        builder.Property(n => n.Note).HasMaxLength(1000);
        builder.Property(n => n.Tags).HasMaxLength(200);

        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.UpdatedAt).IsRequired();

        builder.HasOne(n => n.Owner)
            .WithMany()
            .HasForeignKey(n => n.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.Friend)
            .WithMany()
            .HasForeignKey(n => n.FriendUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // BR-FRIEND-NOTE-01: 1 owner chỉ có 1 note cho 1 friend
        builder.HasIndex(n => new { n.OwnerUserId, n.FriendUserId }).IsUnique();
        builder.HasIndex(n => n.OwnerUserId);
    }
}

public class FriendReportConfiguration : IEntityTypeConfiguration<FriendReport>
{
    public void Configure(EntityTypeBuilder<FriendReport> builder)
    {
        builder.ToTable("FriendReports");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Category).HasConversion<int>().IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Pending");
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(1000);
        builder.Property(r => r.AdminNote).HasMaxLength(1000);

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.ReviewedAt);

        builder.HasOne(r => r.Reporter)
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.TargetUser)
            .WithMany()
            .HasForeignKey(r => r.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.ReporterId, r.TargetUserId })
            .HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.TargetUserId);
    }
}
