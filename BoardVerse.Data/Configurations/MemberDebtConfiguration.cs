using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF Core configuration cho <see cref="MemberDebt"/>.
/// M2/C2.16 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16.
/// (2026-10-01)
/// </summary>
public class MemberDebtConfiguration : IEntityTypeConfiguration<MemberDebt>
{
    public void Configure(EntityTypeBuilder<MemberDebt> builder)
    {
        builder.ToTable("MemberDebts");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.MemberId).IsRequired();
        builder.Property(d => d.SessionId).IsRequired();

        builder.Property(d => d.UserId)
            .IsRequired(false);

        // AmountBvc legacy — không dùng trong M2/C2.16 (Debt là cash), nhưng vẫn column để schema sẵn sàng mở rộng.
        builder.Property(d => d.AmountBvc)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(d => d.AmountCash)
            .HasColumnType("numeric(18, 0)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(d => d.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(Core.Enum.DebtStatus.Pending);

        builder.Property(d => d.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(d => d.ResolvedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(d => d.ResolvedByUserId)
            .IsRequired(false);

        // ===== Indexes cho query phổ biến =====
        builder.HasIndex(d => d.MemberId)
            .HasDatabaseName("IX_MemberDebts_MemberId");

        builder.HasIndex(d => d.SessionId)
            .HasDatabaseName("IX_MemberDebts_SessionId");

        builder.HasIndex(d => d.Status)
            .HasDatabaseName("IX_MemberDebts_Status");

        // Composite index cho query "nợ của user đang pending" (UI debt dashboard)
        builder.HasIndex(d => new { d.UserId, d.Status })
            .HasDatabaseName("IX_MemberDebts_UserId_Status");

        // ===== Relationships =====
        builder.HasOne(d => d.Member)
            .WithMany()
            .HasForeignKey(d => d.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Session)
            .WithMany()
            .HasForeignKey(d => d.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}