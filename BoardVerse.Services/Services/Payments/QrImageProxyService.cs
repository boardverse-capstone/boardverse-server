using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Payments;

/// <summary>
/// Proxy ảnh QR từ VietQR (vietqr.app) về server-side để bypass CORS cho Flutter Web.
/// Backend fetch bytes → trả Base64 cho client (mobile + web đều dùng được).
/// </summary>
public interface IQrImageProxyService
{
    /// <summary>
    /// Fetch ảnh QR PNG từ <paramref name="qrUrl"/> và encode Base64.
    /// Trả null nếu fetch fail / timeout / non-image response — caller nên log + tiếp tục flow
    /// (không block top-up vì QR là bonus, client vẫn có <c>QrUrl</c> để tự load).
    /// </summary>
    /// <param name="qrUrl">URL QR đầy đủ từ vietqr.app/img (do IVietQrClient sinh ra).</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>Base64 string (PNG), hoặc null nếu upstream lỗi.</returns>
    Task<string?> FetchAsBase64Async(string qrUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch ảnh QR PNG thẳng về <see cref="Stream"/> để controller trả binary
    /// (cho endpoint fallback <c>GET /wallet/topup/{orderId}/qr-image</c>).
    /// Caller phải dispose stream sau khi copy vào response body.
    /// </summary>
    /// <returns>Stream chứa PNG bytes, hoặc null nếu upstream lỗi.</returns>
    Task<Stream?> FetchAsStreamAsync(string qrUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation dùng typed <see cref="HttpClient"/> từ DI.
/// Timeout mặc định 5s (không block top-up flow nếu VietQR chậm).
/// User-Agent header giả lập trình duyệt phổ biến để một số CDN không reject request server-to-server.
/// </summary>
public class QrImageProxyService : IQrImageProxyService
{
    internal const int FetchTimeoutSeconds = 5;
    internal const long MaxResponseBytes = 2 * 1024 * 1024; // 2 MB cap — ảnh QR compact ~5-15 KB, nhưng tránh OOM nếu upstream trả blob bất thường.

    private readonly HttpClient _http;
    private readonly ILogger<QrImageProxyService> _logger;

    public QrImageProxyService(HttpClient http, ILogger<QrImageProxyService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string?> FetchAsBase64Async(string qrUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(qrUrl))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(FetchTimeoutSeconds));

            using var response = await _http.GetAsync(qrUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "QR image proxy non-success. Url={Url}, StatusCode={StatusCode}",
                    qrUrl, (int)response.StatusCode);
                return null;
            }

            // Chỉ nhận image/* — chặn upstream trả HTML error page lẫn vào base64.
            if (!IsImageContentType(response.Content.Headers.ContentType))
            {
                _logger.LogWarning(
                    "QR image proxy unexpected content-type. Url={Url}, ContentType={ContentType}",
                    qrUrl, response.Content.Headers.ContentType?.MediaType);
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var ms = new MemoryStream();
            await CopyWithLimitAsync(stream, ms, cts.Token);

            if (ms.Length == 0)
            {
                _logger.LogWarning("QR image proxy empty body. Url={Url}", qrUrl);
                return null;
            }

            return Convert.ToBase64String(ms.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // timeout từ linked CTS, không phải client cancel
            _logger.LogWarning("QR image proxy timeout. Url={Url}, TimeoutSeconds={Timeout}",
                qrUrl, FetchTimeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QR image proxy failed. Url={Url}", qrUrl);
            return null;
        }
    }

    public async Task<Stream?> FetchAsStreamAsync(string qrUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(qrUrl))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(FetchTimeoutSeconds));

            using var response = await _http.GetAsync(qrUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "QR image stream proxy non-success. Url={Url}, StatusCode={StatusCode}",
                    qrUrl, (int)response.StatusCode);
                return null;
            }

            if (!IsImageContentType(response.Content.Headers.ContentType))
            {
                _logger.LogWarning(
                    "QR image stream proxy unexpected content-type. Url={Url}, ContentType={ContentType}",
                    qrUrl, response.Content.Headers.ContentType?.MediaType);
                return null;
            }

            var sourceStream = await response.Content.ReadAsStreamAsync(cts.Token);
            var memoryStream = new MemoryStream();
            await CopyWithLimitAsync(sourceStream, memoryStream, cts.Token);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("QR image stream proxy timeout. Url={Url}", qrUrl);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QR image stream proxy failed. Url={Url}", qrUrl);
            return null;
        }
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
                    $"QR image response exceeded {MaxResponseBytes} bytes cap.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
