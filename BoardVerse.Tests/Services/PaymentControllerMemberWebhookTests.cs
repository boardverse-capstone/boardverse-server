using System.IO;
using System.Text;
using System.Text.Json;
using BoardVerse.API.Controllers;
using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="PaymentController.ProcessMemberPaymentWebhook"/>.
/// GAP FIX: Kiểm tra signature verification được gọi trước khi xử lý webhook.
/// </summary>
public class PaymentControllerMemberWebhookTests
{
    private readonly Mock<ISplitBillService> _splitBillServiceMock;
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<IHostEnvironment> _envMock;
    private readonly Mock<ILogger<PaymentController>> _loggerMock;
    private readonly PaymentController _controller;

    public PaymentControllerMemberWebhookTests()
    {
        _splitBillServiceMock = new Mock<ISplitBillService>();
        _paymentServiceMock = new Mock<IPaymentService>();
        _envMock = new Mock<IHostEnvironment>();
        _loggerMock = new Mock<ILogger<PaymentController>>();

        // Mặc định: Development environment (skip signature check)
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");

        _controller = new PaymentController(
            paymentService: _paymentServiceMock.Object,
            depositService: null!,
            manualPaymentService: null!,
            splitBillService: _splitBillServiceMock.Object,
            sePayClient: null!,
            env: _envMock.Object,
            logger: _loggerMock.Object);

        // Setup HttpContext với memory stream
        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #region Signature Verification — Production Mode

    [Fact]
    public async Task ProcessMemberPaymentWebhook_ProductionMode_SignatureValid_CallsSplitBillService()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Production");

        _paymentServiceMock.Setup(c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null));

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = Guid.NewGuid(),
            Amount = 100000m,
            Status = "success"
        };
        var body = JsonSerializer.Serialize(webhook);
        SetupRequestBody(body);

        // Act
        var result = await _controller.ProcessMemberPaymentWebhook();

        // Assert
        var okResult = Assert.IsType<OkResult>(result);
        _splitBillServiceMock.Verify(
            s => s.ProcessMemberQrWebhookAsync(It.IsAny<MemberPaymentWebhookDto>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Khi signature hợp lệ, webhook phải được gửi xuống SplitBillService");
    }

    [Fact]
    public async Task ProcessMemberPaymentWebhook_ProductionMode_SignatureInvalid_Returns401()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Production");

        _paymentServiceMock.Setup(c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "SePay webhook signature invalid."));

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = Guid.NewGuid(),
            Amount = 100000m,
            Status = "success"
        };
        var body = JsonSerializer.Serialize(webhook);
        SetupRequestBody(body);

        // Act
        var result = await _controller.ProcessMemberPaymentWebhook();

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorizedResult.StatusCode);

        // SplitBillService KHÔNG được gọi khi signature invalid
        _splitBillServiceMock.Verify(
            s => s.ProcessMemberQrWebhookAsync(It.IsAny<MemberPaymentWebhookDto>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Khi signature không hợp lệ, webhook KHÔNG được xử lý");
    }

    [Fact]
    public async Task ProcessMemberPaymentWebhook_ProductionMode_CallsVerifyWebhookRequestAsync_WithCorrectRequest()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Production");

        _paymentServiceMock.Setup(c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null));

        var webhook = new MemberPaymentWebhookDto
        {
            OrderId = "BV-MEMBER-123",
            MemberId = Guid.NewGuid(),
            Amount = 50000m,
            Status = "success"
        };
        var body = JsonSerializer.Serialize(webhook);
        SetupRequestBody(body, signature: "sha256=abc123", timestamp: "1234567890");

        // Act
        await _controller.ProcessMemberPaymentWebhook();

        // Assert — VerifyWebhookRequestAsync được gọi với đúng rawBody
        _paymentServiceMock.Verify(
            c => c.VerifyWebhookRequestAsync(
                It.Is<SePayWebhookVerificationRequest>(r =>
                    r.RawBody == body &&
                    r.Signature == "sha256=abc123" &&
                    r.Timestamp == "1234567890"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "VerifyWebhookRequestAsync phải được gọi với đúng rawBody, signature và timestamp");
    }

    [Fact]
    public async Task ProcessMemberPaymentWebhook_ProductionMode_CallsVerifyWebhookRequestAsync_ExactlyOnce()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Production");

        _paymentServiceMock.Setup(c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null));

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = Guid.NewGuid(),
            Amount = 100000m,
            Status = "success"
        };
        SetupRequestBody(JsonSerializer.Serialize(webhook));

        // Act
        await _controller.ProcessMemberPaymentWebhook();

        // Assert
        _paymentServiceMock.Verify(
            c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "VerifyWebhookRequestAsync phải được gọi đúng 1 lần");
    }

    #endregion

    #region Signature Verification — Development Mode

    [Fact]
    public async Task ProcessMemberPaymentWebhook_DevelopmentMode_SkipsSignatureVerification_StillProcessesWebhook()
    {
        // Arrange: Development mode KHÔNG gọi VerifyWebhookRequestAsync
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = Guid.NewGuid(),
            Amount = 100000m,
            Status = "success"
        };
        SetupRequestBody(JsonSerializer.Serialize(webhook));

        // Act
        var result = await _controller.ProcessMemberPaymentWebhook();

        // Assert
        Assert.IsType<OkResult>(result);

        // VerifyWebhookRequestAsync KHÔNG được gọi trong development
        _paymentServiceMock.Verify(
            c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "Development mode phải skip signature verification");

        // Nhưng webhook vẫn được xử lý
        _splitBillServiceMock.Verify(
            s => s.ProcessMemberQrWebhookAsync(It.IsAny<MemberPaymentWebhookDto>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Development mode vẫn xử lý webhook bình thường");
    }

    #endregion

    #region Payload Validation

    [Fact]
    public async Task ProcessMemberPaymentWebhook_EmptyPayload_Returns400()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");
        SetupRequestBody("");

        // Act
        var result = await _controller.ProcessMemberPaymentWebhook();

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);

        // SplitBillService KHÔNG được gọi
        _splitBillServiceMock.Verify(
            s => s.ProcessMemberQrWebhookAsync(It.IsAny<MemberPaymentWebhookDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMemberPaymentWebhook_InvalidJson_Returns400()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");
        SetupRequestBody("{ invalid json }");

        // Act
        var result = await _controller.ProcessMemberPaymentWebhook();

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task ProcessMemberPaymentWebhook_ValidPayload_PassesParsedDtoToSplitBillService()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");

        MemberPaymentWebhookDto? capturedWebhook = null;
        _splitBillServiceMock
            .Setup(s => s.ProcessMemberQrWebhookAsync(
                It.IsAny<MemberPaymentWebhookDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<MemberPaymentWebhookDto, CancellationToken>((w, _) => capturedWebhook = w)
            .Returns(Task.CompletedTask);

        var expectedMemberId = Guid.NewGuid();
        var webhook = new MemberPaymentWebhookDto
        {
            OrderId = $"BV-MEMBER-{expectedMemberId:N}",
            MemberId = expectedMemberId,
            Amount = 75000m,
            Status = "success",
            GatewayTransactionId = "TXN-TEST-001",
            ReferenceCode = "REF-001"
        };
        SetupRequestBody(JsonSerializer.Serialize(webhook));

        // Act
        await _controller.ProcessMemberPaymentWebhook();

        // Assert
        Assert.NotNull(capturedWebhook);
        Assert.Equal(expectedMemberId, capturedWebhook!.MemberId);
        Assert.Equal(75000m, capturedWebhook.Amount);
        Assert.Equal("success", capturedWebhook.Status);
        Assert.Equal("TXN-TEST-001", capturedWebhook.GatewayTransactionId);
    }

    #endregion

    #region Authorization Header Extraction

    [Fact]
    public async Task ProcessMemberPaymentWebhook_ApiKeyAuthHeader_ExtractsTokenCorrectly()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Production");

        SePayWebhookVerificationRequest? capturedRequest = null;
        _paymentServiceMock.Setup(c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<SePayWebhookVerificationRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync((true, (string?)null));

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = Guid.NewGuid(),
            Amount = 100000m,
            Status = "success"
        };
        SetupRequestBody(
            JsonSerializer.Serialize(webhook),
            signature: null,
            timestamp: null,
            authHeader: "Apikey my-secret-token");

        // Act
        await _controller.ProcessMemberPaymentWebhook();

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("my-secret-token", capturedRequest!.Signature);
    }

    [Fact]
    public async Task ProcessMemberPaymentWebhook_HmacSignatureHeader_ExtractsCorrectly()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns("Production");

        SePayWebhookVerificationRequest? capturedRequest = null;
        _paymentServiceMock.Setup(c => c.VerifyWebhookRequestAsync(
                It.IsAny<SePayWebhookVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<SePayWebhookVerificationRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync((true, (string?)null));

        var webhook = new MemberPaymentWebhookDto
        {
            MemberId = Guid.NewGuid(),
            Amount = 100000m,
            Status = "success"
        };
        SetupRequestBody(
            JsonSerializer.Serialize(webhook),
            signature: "sha256=hmacabc123",
            timestamp: "1234567890");

        // Act
        await _controller.ProcessMemberPaymentWebhook();

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("sha256=hmacabc123", capturedRequest!.Signature);
        Assert.Equal("1234567890", capturedRequest.Timestamp);
    }

    #endregion

    #region Test Helpers

    private void SetupRequestBody(
        string body,
        string? signature = null,
        string? timestamp = null,
        string? authHeader = null)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        _controller.HttpContext.Request.Body = stream;
        _controller.HttpContext.Request.ContentLength = stream.Length;
        _controller.HttpContext.Request.ContentType = "application/json";

        var headers = _controller.HttpContext.Request.Headers;

        if (signature != null)
            headers["X-SePay-Signature"] = signature;
        if (timestamp != null)
            headers["X-SePay-Timestamp"] = timestamp;
        if (authHeader != null)
            headers["Authorization"] = authHeader;
    }

    #endregion
}
