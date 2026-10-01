using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class ActiveSessionMemberConfiguration : IEntityTypeConfiguration<ActiveSessionMember>
    {
        public void Configure(EntityTypeBuilder<ActiveSessionMember> builder)
        {
            builder.ToTable("ActiveSessionMembers");

            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
            builder.Property(m => m.ActiveSessionId).IsRequired();

            builder.Property(m => m.UserId)
                .IsRequired(false);

            builder.Property(m => m.JoinedAt).IsRequired();

            // Optional phone for GuestSlot (BR-13). Max 20 chars to fit VN (+84) international.
            builder.Property(m => m.GuestPhoneNumber)
                .HasMaxLength(20)
                .IsRequired(false);

            // C17: UpdatedAt as concurrency token for optimistic concurrency on penalty/financial updates.
            builder.Property(m => m.UpdatedAt)
                .HasDefaultValueSql("now() at time zone 'utc'")
                .IsConcurrencyToken();

            builder.Property(m => m.Status)
                .HasConversion<int>()
                .IsRequired();

            // H2: Decimal precision on financial fields (BR-22 per-member deposit + BR-14 penalty).
            builder.Property(m => m.PenaltyAmount).HasColumnType("numeric(18,2)");
            builder.Property(m => m.DepositAppliedAmount).HasColumnType("numeric(18,2)");
            builder.Property(m => m.Subtotal).HasColumnType("numeric(18,2)");
            builder.Property(m => m.TotalAmount).HasColumnType("numeric(18,2)");

            // Lobby merge: track lobby/reservation gốc khi member nhảy nhóm
            builder.Property(m => m.OriginalLobbyId);
            builder.Property(m => m.OriginalReservationId);
            builder.Property(m => m.MergedFromLobbyId);
            builder.Property(m => m.MergedAt);

            // BR-12/BR-22: Host role (1 host per session).
            builder.Property(m => m.IsHost)
                .IsRequired()
                .HasDefaultValue(false);

            builder.HasOne(m => m.ActiveSession)
                .WithMany(s => s.Members)
                .HasForeignKey(m => m.ActiveSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(m => new { m.ActiveSessionId, m.UserId })
                .IsUnique()
                .HasFilter("\"Status\" != 2");

            builder.HasIndex(m => m.UserId);

            // Split Bill: QR payment info for mobile display
            builder.Property(m => m.QrImageUrl)
                .HasMaxLength(2000)
                .IsRequired(false);
            builder.Property(m => m.QrPaymentUrl)
                .HasMaxLength(2000)
                .IsRequired(false);
            builder.Property(m => m.QrOrderId)
                .HasMaxLength(100)
                .IsRequired(false);
            builder.Property(m => m.QrTransferContent)
                .HasMaxLength(500)
                .IsRequired(false);

            // ===== M1 / Option A: Per-member deposit refund tracking =====
            // docs/design/host-deposit-discount-and-bvc-payment-design.md §B1.3

            builder.Property(m => m.DepositRefundedAt)
                .HasColumnType("timestamp with time zone");

            builder.Property(m => m.DepositRefundReason)
                .HasMaxLength(500)
                .IsRequired(false);

            builder.Property(m => m.DepositRefundLedgerId);

            // Index cho query "members đã được refund" (filter khi audit)
            builder.HasIndex(m => m.DepositRefundedAt)
                .HasDatabaseName("IX_ActiveSessionMembers_DepositRefundedAt");

            // ===== M2/C2.16: Force-close tracking (Gap #33) =====
            // docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16

            builder.Property(m => m.NoShowAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired(false);

            builder.Property(m => m.NoShowReason)
                .HasMaxLength(500)
                .IsRequired(false);

            builder.Property(m => m.PaidByHostAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired(false);

            builder.Property(m => m.PaidByHostUserId)
                .IsRequired(false);

            // Index cho query audit "members bị NoShow" trong 30 ngày.
            builder.HasIndex(m => m.NoShowAt)
                .HasDatabaseName("IX_ActiveSessionMembers_NoShowAt");
        }
    }
}
