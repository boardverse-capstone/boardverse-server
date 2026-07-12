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

            builder.Property(m => m.Status)
                .HasConversion<int>()
                .IsRequired();

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
