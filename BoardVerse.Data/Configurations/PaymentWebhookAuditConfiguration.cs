using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// GAP-10 Fix: Configuration cho PaymentWebhookAudit table.
/// Lưu payload + kết quả xử lý cho mọi SePay webhook nhận được.
/// </summary>
public class PaymentWebhookAuditConfiguration : IEntityTypeConfiguration<PaymentWebhookAudit>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookAudit> b)
    {
        b.ToTable("PaymentWebhookAudits");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderId).IsRequired().HasMaxLength(64);
        b.Property(x => x.GatewayTransactionId).HasMaxLength(128);
        b.Property(x => x.Currency).HasMaxLength(8);
        b.Property(x => x.Status).IsRequired().HasMaxLength(32);
        b.Property(x => x.Result).IsRequired().HasMaxLength(64);
        b.Property(x => x.Detail).HasMaxLength(2000);
        b.Property(x => x.Payload).HasColumnType("text"); // raw JSON
        b.Property(x => x.RemoteIp).HasMaxLength(64);

        // GAP-11 Fix: index cho query đếm amount_mismatch theo thời gian
        b.HasIndex(x => new { x.Result, x.ProcessedAt });
        b.HasIndex(x => x.SessionId);
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.ProcessedAt);
    }
}