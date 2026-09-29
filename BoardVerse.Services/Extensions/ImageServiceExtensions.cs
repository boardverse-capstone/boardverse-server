using BoardVerse.Services.Services.Images;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services.Extensions;

/// <summary>
/// DI registration cho image proxy services.
/// Hiện tại có <see cref="IThumbnailProxyService"/> để bypass CORS cho ảnh BGG CDN
/// trên Flutter Web (CanvasKit taint canvas khi upstream không trả
/// <c>Access-Control-Allow-Origin</c>).
/// </summary>
public static class ImageServiceExtensions
{
    public static IServiceCollection AddBoardVerseImages(this IServiceCollection services)
    {
        // ThumbnailProxyService dùng HttpClient chung — KHÔNG register như HttpClient factory
        // ở đây, vì sẽ dùng service chung. Caller nên DI IHttpClientFactory nếu cần customize.
        // Theo pattern của QrImageProxyService: typed HttpClient injection.
        services.AddHttpClient<IThumbnailProxyService, ThumbnailProxyService>(client =>
            {
                // UA giả lập trình duyệt phổ biệt — tránh CDN reject server-to-server.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        return services;
    }
}