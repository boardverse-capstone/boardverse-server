using System.Security.Cryptography;
using System.Text;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.Services.Payments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="SePayClient.VerifyWebhookAsync"/> — 3 mode:
/// - None (dev only): luôn pass khi non-production.
/// - ApiKey: so sánh trực tiếp WebhookToken qua constant-time.
/// - HmacSha256: HMAC-SHA256({timestamp}.{rawBody}, SecretKey) + timestamp anti-replay.
/// </summary>
public class SePayClientWebhookVerificationTests
{
    private const string TestSecretKey = "whsec_a1b2c3d4e5f6";
    private const string TestWebhookToken = "test-webhook-token-abc123";

    [Fact]
    public async Task VerifyWebhookAsync_NoneMode_NonProduction_ReturnsTrue()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.None,
            IsActive = true
        });

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(null, null, "any body"));

        result.Should().BeTrue("None mode bypass verification in dev (default env is Development for tests).");
    }

    [Fact]
    public async Task VerifyWebhookAsync_ApiKeyMode_ValidSignature_ReturnsTrue()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.ApiKey,
            WebhookToken = TestWebhookToken,
            IsActive = true
        });

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: TestWebhookToken,
                Timestamp: null,
                RawBody: "{\"id\":1}"));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyWebhookAsync_ApiKeyMode_InvalidSignature_ReturnsFalse()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.ApiKey,
            WebhookToken = TestWebhookToken,
            IsActive = true
        });

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: "wrong-token",
                Timestamp: null,
                RawBody: "{\"id\":1}"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookAsync_ApiKeyMode_EmptyWebhookToken_ReturnsFalse()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.ApiKey,
            WebhookToken = null,
            IsActive = true
        });

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: TestWebhookToken,
                Timestamp: null,
                RawBody: "{\"id\":1}"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookAsync_HmacSha256_ValidSignature_ReturnsTrue()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.HmacSha256,
            SecretKey = TestSecretKey,
            IsActive = true
        });

        var rawBody = "{\"id\":1,\"orderId\":\"BV123\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = ComputeSePayHmacSignature(TestSecretKey, timestamp, rawBody);

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: signature,
                Timestamp: timestamp,
                RawBody: rawBody));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyWebhookAsync_HmacSha256_InvalidSignature_ReturnsFalse()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.HmacSha256,
            SecretKey = TestSecretKey,
            IsActive = true
        });

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: "sha256=deadbeef",
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                RawBody: "{\"id\":1}"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookAsync_HmacSha256_TimestampTooOld_ReturnsFalse()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.HmacSha256,
            SecretKey = TestSecretKey,
            IsActive = true
        });

        var rawBody = "{\"id\":1}";
        // 600s ago — outside ±300s window.
        var oldTimestamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString();
        var signature = ComputeSePayHmacSignature(TestSecretKey, oldTimestamp, rawBody);

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: signature,
                Timestamp: oldTimestamp,
                RawBody: rawBody));

        result.Should().BeFalse("timestamps older than 300s must be rejected (anti-replay).");
    }

    [Fact]
    public async Task VerifyWebhookAsync_HmacSha256_TimestampInFuture_ReturnsFalse()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.HmacSha256,
            SecretKey = TestSecretKey,
            IsActive = true
        });

        var rawBody = "{\"id\":1}";
        var futureTimestamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600).ToString();
        var signature = ComputeSePayHmacSignature(TestSecretKey, futureTimestamp, rawBody);

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: signature,
                Timestamp: futureTimestamp,
                RawBody: rawBody));

        result.Should().BeFalse("timestamps in future >300s must be rejected.");
    }

    [Fact]
    public async Task VerifyWebhookAsync_HmacSha256_MissingTimestamp_ReturnsFalse()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.HmacSha256,
            SecretKey = TestSecretKey,
            IsActive = true
        });

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: "sha256=anything",
                Timestamp: null,
                RawBody: "{\"id\":1}"));

        result.Should().BeFalse("missing X-SePay-Timestamp must be rejected.");
    }

    [Fact]
    public async Task VerifyWebhookAsync_HmacSha256_EmptySecretKey_ReturnsFalse()
    {
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.HmacSha256,
            SecretKey = null,
            IsActive = true
        });

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: "sha256=anything",
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                RawBody: "{\"id\":1}"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookAsync_HmacSha256_BodyTampering_ReturnsFalse()
    {
        // Verify that if attacker modifies body, HMAC signature no longer matches.
        var (client, _, _) = BuildClient(new SePayAccount
        {
            AccountType = SePayAccountType.Master,
            WebhookAuthType = SePayWebhookAuthType.HmacSha256,
            SecretKey = TestSecretKey,
            IsActive = true
        });

        var originalBody = "{\"id\":1,\"amount\":100}";
        var tamperedBody = "{\"id\":1,\"amount\":999999}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signatureForOriginal = ComputeSePayHmacSignature(TestSecretKey, timestamp, originalBody);

        var result = await client.VerifyWebhookAsync(
            new SePayWebhookVerificationRequest(
                Signature: signatureForOriginal,
                Timestamp: timestamp,
                RawBody: tamperedBody));

        result.Should().BeFalse("tampered body must produce different HMAC, signature mismatch.");
    }

    // ===== Helpers =====

    private static (SePayClient, Mock<ISePayAccountRepository> repo, HttpClient http) BuildClient(SePayAccount account)
    {
        var repoMock = new Mock<ISePayAccountRepository>();
        repoMock.Setup(r => r.GetMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var http = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var client = new SePayClient(repoMock.Object, http, NullLogger<SePayClient>.Instance);
        return (client, repoMock, http);
    }

    /// <summary>
    /// Reconstruct chữ ký SePay theo công thức:
    /// <c>sha256=HMAC-SHA256(secretKey, "{timestamp}.{rawBody}").ToHex(LowerCase)</c>.
    /// </summary>
    internal static string ComputeSePayHmacSignature(string secretKey, string timestamp, string rawBody)
    {
        var messageBytes = Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(messageBytes);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}