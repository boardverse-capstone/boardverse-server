using System.Net;
using System.Net.Http.Headers;
using BoardVerse.Services.Services.Images;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="ThumbnailProxyService"/> — proxy ảnh BGG CDN về server-side
/// để bypass CORS cho Flutter Web (CanvasKit taint canvas).
/// Test case: URL rỗng, URL không hợp lệ, host không whitelist, host whitelist, non-image, non-2xx, timeout, oversize.
/// </summary>
public class ThumbnailProxyServiceTests
{
    private const string ValidBggUrl =
        "https://cf.geekdo-images.com/vNFe4JkhKAERzi4T0Ntwpw__micro@2x/img/8PnkptdfHcjprbqX7BxlH6dmCXg=/fit-in/128x128/filters:strip_icc()/pic8234167.png";

    private static readonly byte[] FakePngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    [Fact]
    public async Task FetchAsync_NullUrl_ReturnsNull()
    {
        var service = BuildService(_ => new HttpResponseMessage());
        var result = await service.FetchAsync(null!);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_EmptyUrl_ReturnsNull()
    {
        var service = BuildService(_ => new HttpResponseMessage());
        var result = await service.FetchAsync(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_WhitespaceUrl_ReturnsNull()
    {
        var service = BuildService(_ => new HttpResponseMessage());
        var result = await service.FetchAsync("   ");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_InvalidScheme_ReturnsNull()
    {
        var service = BuildService(_ => new HttpResponseMessage());
        var result = await service.FetchAsync("ftp://cf.geekdo-images.com/pic.png");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_RelativeUrl_ReturnsNull()
    {
        var service = BuildService(_ => new HttpResponseMessage());
        var result = await service.FetchAsync("/some/relative/path.png");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_NonWhitelistedHost_ReturnsNull()
    {
        var service = BuildService(_ => new HttpResponseMessage());
        // evil-cf.geekdo-images.com.attacker.com không nằm trong whitelist (exact match).
        var result = await service.FetchAsync("https://evil-cf.geekdo-images.com.attacker.com/pic.png");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_DifferentHost_ReturnsNull()
    {
        var service = BuildService(_ => new HttpResponseMessage());
        var result = await service.FetchAsync("https://example.com/some-image.png");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_NonSuccessStatusCode_ReturnsNull()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.NotFound);
            msg.Content = new ByteArrayContent(FakePngBytes);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return msg;
        });

        var result = await service.FetchAsync(ValidBggUrl);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_NonImageContentType_ReturnsNull()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(FakePngBytes);
            // HTML — sẽ bị reject vì không phải image/*
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return msg;
        });

        var result = await service.FetchAsync(ValidBggUrl);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_EmptyBody_ReturnsNull()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(Array.Empty<byte>());
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return msg;
        });

        var result = await service.FetchAsync(ValidBggUrl);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_ValidPng_ReturnsStreamWithContentType()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(FakePngBytes);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return msg;
        });

        var result = await service.FetchAsync(ValidBggUrl);
        result.Should().NotBeNull();
        result!.Value.ContentType.Should().Be("image/png");

        await using var ms = new MemoryStream();
        await result.Value.Stream.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(FakePngBytes);
    }

    [Fact]
    public async Task FetchAsync_ValidJpeg_ReturnsJpegContentType()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(FakePngBytes);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return msg;
        });

        var result = await service.FetchAsync(ValidBggUrl);
        result.Should().NotBeNull();
        result!.Value.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task FetchAsync_AllowedHost_CfGeekdoCom_ReturnsStream()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(FakePngBytes);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return msg;
        });

        var result = await service.FetchAsync("https://cf.geekdo.com/pic.png");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FetchAsync_AllowedHost_BoardgamegeekCom_ReturnsStream()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(FakePngBytes);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return msg;
        });

        var result = await service.FetchAsync("https://images.boardgamegeek.com/pic.png");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FetchAsync_HostCaseInsensitive_ReturnsStream()
    {
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(FakePngBytes);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return msg;
        });

        // Host phải được match case-insensitive — CDN đôi lúc trả uppercase.
        var result = await service.FetchAsync("https://CF.GEEKDO-IMAGES.COM/pic.png");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FetchAsync_OversizedBody_ReturnsNull()
    {
        // 5 MB cap + 1 byte = vượt limit → service return null.
        var oversized = new byte[ThumbnailProxyService.MaxResponseBytes + 1];

        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(oversized);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return msg;
        });

        var result = await service.FetchAsync(ValidBggUrl);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_OctetStreamContentType_Accepted()
    {
        // Một số CDN trả application/octet-stream thay vì image/png — chấp nhận fallback.
        var service = BuildService(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Content = new ByteArrayContent(FakePngBytes);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return msg;
        });

        var result = await service.FetchAsync(ValidBggUrl);
        result.Should().NotBeNull();
    }

    /// <summary>
    /// Helper tạo <see cref="ThumbnailProxyService"/> với HttpMessageHandler giả lập
    /// để kiểm soát hoàn toàn response trả về.
    /// </summary>
    private static ThumbnailProxyService BuildService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new MockHandler(responder);
        var http = new HttpClient(handler)
        {
            // Service tự CancelAfter trong code → set timeout dài để không double-cancel.
            Timeout = TimeSpan.FromSeconds(30),
        };
        return new ThumbnailProxyService(http, NullLogger<ThumbnailProxyService>.Instance);
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
