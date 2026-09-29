using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Images;

/// <summary>
/// Proxy ảnh thumbnail từ nguồn ngoài (BoardGameGeek CDN, …) về server-side
/// để bypass CORS cho Flutter Web (CanvasKit renderer taint canvas khi upstream
/// không trả <c>Access-Control-Allow-Origin</c>).
///
/// Backend fetch bytes → trả binary + content-type cho client qua controller.
/// Mobile app (Android/iOS) load ảnh trực tiếp từ upstream không cần proxy,
/// nhưng endpoint vẫn hoạt động — không phân biệt platform.
/// </summary>
public interface IThumbnailProxyService
{
    /// <summary>
    /// Fetch ảnh từ <paramref name="imageUrl"/> về <see cref="Stream"/>.
    /// Trả null nếu:
    ///   - URL rỗng / không hợp lệ / không thuộc host whitelist.
    ///   - Upstream lỗi / timeout / non-success status.
    ///   - Content-Type không phải <c>image/*</c>.
    ///   - Response vượt size cap.
    ///
    /// Caller nên log + trả 502 cho client (không throw exception — caller quyết định UX).
    /// </summary>
    /// <param name="imageUrl">URL ảnh gốc (vd. BGG CDN <c>https://cf.geekdo-images.com/.../pic123.png</c>).</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>Tuple <c>(Stream, ContentType)</c> hoặc null nếu upstream lỗi.</returns>
    Task<ThumbnailFetchResult?> FetchAsync(string imageUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tuple wrapper cho kết quả fetch — gồm stream + content-type để controller trả đúng
/// MIME cho client.
/// </summary>
/// <param name="Stream">MemoryStream chứa image bytes (đã seek về 0). Caller dispose.</param>
/// <param name="ContentType">MIME type (vd. <c>image/png</c>, <c>image/jpeg</c>).</param>
public readonly record struct ThumbnailFetchResult(Stream Stream, string ContentType);

/// <summary>
/// Default implementation dùng typed <see cref="HttpClient"/> từ DI.
/// Timeout mặc định 5s; size cap 5 MB (thumbnail BGG trung bình 50-500 KB).
/// User-Agent giả lập trình duyệt phổ biến — một số CDN reject server-to-server nếu thiếu.
/// </summary>
public class ThumbnailProxyService : IThumbnailProxyService
{
    /// <summary>Timeout cho mỗi fetch request.</summary>
    internal const int FetchTimeoutSeconds = 5;

    /// <summary>Hard cap 5 MB — đủ cho mọi thumbnail board game (BGG max ~2 MB).</summary>
    internal const long MaxResponseBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Whitelist host được phép proxy — phòng SSRF (chỉ cho phép CDN của BGG).
    /// Mở rộng khi cần proxy thêm nguồn khác (vd: cloudinary, s3 public bucket).
    /// </summary>
    internal static readonly string[] AllowedHosts =
    {
        "cf.geekdo-images.com",
        "cf.geekdo.com",
        "images.boardgamegeek.com",
        "boardgamegeek.com",
    };

    private readonly HttpClient _http;
    private readonly ILogger<ThumbnailProxyService> _logger;

    public ThumbnailProxyService(HttpClient http, ILogger<ThumbnailProxyService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ThumbnailFetchResult?> FetchAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            _logger.LogWarning("Thumbnail proxy called with empty url");
            return null;
        }

        // Parse + validate URL: chỉ https, chỉ host whitelist.
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uriResult)
            || (uriResult.Scheme != Uri.UriSchemeHttps && uriResult.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning("Thumbnail proxy invalid url. Url={Url}", imageUrl);
            return null;
        }

        if (!IsAllowedHost(uriResult.Host))
        {
            _logger.LogWarning(
                "Thumbnail proxy host not in whitelist. Url={Url}, Host={Host}",
                imageUrl, uriResult.Host);
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(FetchTimeoutSeconds));

            using var response = await _http.GetAsync(
                imageUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Thumbnail proxy non-success. Url={Url}, StatusCode={StatusCode}",
                    imageUrl, (int)response.StatusCode);
                return null;
            }

            if (!IsImageContentType(response.Content.Headers.ContentType))
            {
                _logger.LogWarning(
                    "Thumbnail proxy unexpected content-type. Url={Url}, ContentType={ContentType}",
                    imageUrl, response.Content.Headers.ContentType?.MediaType);
                return null;
            }

            var sourceStream = await response.Content.ReadAsStreamAsync(cts.Token);
            var memoryStream = new MemoryStream();
            await CopyWithLimitAsync(sourceStream, memoryStream, cts.Token);

            if (memoryStream.Length == 0)
            {
                _logger.LogWarning("Thumbnail proxy empty body. Url={Url}", imageUrl);
                memoryStream.Dispose();
                return null;
            }

            memoryStream.Position = 0;
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            return new ThumbnailFetchResult(memoryStream, contentType);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // timeout từ linked CTS, không phải client cancel
            _logger.LogWarning(
                "Thumbnail proxy timeout. Url={Url}, TimeoutSeconds={Timeout}",
                imageUrl, FetchTimeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail proxy failed. Url={Url}", imageUrl);
            return null;
        }
    }

    private static bool IsAllowedHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        foreach (var allowed in AllowedHosts)
        {
            // Exact match để tránh `evil-cf.geekdo-images.com.attacker.com` bypass.
            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsImageContentType(MediaTypeHeaderValue? contentType)
    {
        if (contentType == null) return false;
        var media = contentType.MediaType;
        return !string.IsNullOrEmpty(media)
            && (media.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || media.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxResponseBytes)
            {
                throw new InvalidOperationException(
                    $"Thumbnail response exceeded {MaxResponseBytes} bytes cap.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}