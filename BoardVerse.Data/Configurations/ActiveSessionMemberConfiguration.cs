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
        }
    }
}
