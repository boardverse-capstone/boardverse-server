using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// POS QR token config — Index unique trên Token, query theo (CafeId, ExpiresAt).
/// Cascade theo ReservationId (set null khi xóa reservation — không xóa token audit).
/// </summary>
public class PosCheckInTokenConfiguration : IEntityTypeConfiguration<PosCheckInToken>
{
    public void Configure(EntityTypeBuilder<PosCheckInToken> builder)
    {
        builder.ToTable("PosCheckInTokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CafeId).IsRequired();
        builder.Property(x => x.Token)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(x => x.CreatedByStaffId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.IsRevoked).IsRequired();

        builder.HasIndex(x => x.Token)
            .IsUnique();

        builder.HasIndex(x => new { x.CafeId, x.ExpiresAt });

        builder.HasOne(x => x.Cafe)
            .WithMany()
            .HasForeignKey(x => x.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Reservation)
            .WithMany()
            .HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CreatedByStaff)
            .WithMany()
            .HasForeignKey(x => x.CreatedByStaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ConsumedByUser)
            .WithMany()
            .HasForeignKey(x => x.ConsumedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}