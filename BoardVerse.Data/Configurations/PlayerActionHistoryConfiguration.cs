using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class PlayerActionHistoryConfiguration : IEntityTypeConfiguration<PlayerActionHistory>
{
    public void Configure(EntityTypeBuilder<PlayerActionHistory> builder)
    {
        builder.ToTable("PlayerActionHistories");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.UserId)
            .IsRequired();

        builder.Property(p => p.ActionType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.ActionBy)
            .IsRequired();

        builder.Property(p => p.Reason)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(p => p.Metadata)
            .HasColumnType("jsonb");

        builder.Property(p => p.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(p => p.UserId)
            .HasDatabaseName("IX_PlayerActionHistories_UserId");

        builder.HasIndex(p => p.CreatedAt)
            .HasDatabaseName("IX_PlayerActionHistories_CreatedAt");
    }
}
