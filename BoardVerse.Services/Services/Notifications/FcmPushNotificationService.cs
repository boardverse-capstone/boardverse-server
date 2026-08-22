using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Notifications;

/// <summary>
/// Firebase Cloud Messaging implementation cho push notification.
/// Khởi tạo FirebaseAdmin app lần đầu (singleton scope) từ
/// <c>FirebaseSettings.CredentialsJson</c>; nếu <c>Enabled=false</c> thì
/// chỉ log payload mà không gọi FCM (cho local dev / integration test).
/// </summary>
public class FcmPushNotificationService : IPushNotificationService, IDisposable
{
    private readonly IDeviceTokenRepository _deviceTokenRepository;
    private readonly FirebaseSettings _settings;
    private readonly ILogger<FcmPushNotificationService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _firebaseInitialized;

    public FcmPushNotificationService(
        IDeviceTokenRepository deviceTokenRepository,
        IOptions<FirebaseSettings> settings,
        ILogger<FcmPushNotificationService> logger)
    {
        _deviceTokenRepository = deviceTokenRepository;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> SendToUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        PushNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation(
                "[FCM-DISABLED] type={Type} users={UserCount} title={Title} body={Body}",
                payload.Type, userIds.Count, payload.Title, payload.Body);
            return 0;
        }

        if (userIds.Count == 0)
        {
            return 0;
        }

        var tokens = await _deviceTokenRepository.GetActiveTokensByUserIdsAsync(userIds);
        if (tokens.Count == 0)
        {
            _logger.LogInformation("[FCM] No active tokens for {UserCount} users (type={Type})",
                userIds.Count, payload.Type);
            return 0;
        }

        await EnsureFirebaseInitializedAsync();

        var message = new MulticastMessage
        {
            Tokens = tokens.Select(t => t.Token).ToList(),
            Notification = new Notification
            {
                Title = payload.Title,
                Body = payload.Body
            },
            Data = payload.Data.ToDictionary(kv => kv.Key, kv => kv.Value),
            Android = new AndroidConfig { Priority = Priority.Normal },
            Apns = new ApnsConfig { Headers = new Dictionary<string, string> { { "apns-priority", "10" } } }
        };

        try
        {
            var messaging = FirebaseMessaging.DefaultInstance;
            var response = await messaging.SendEachForMulticastAsync(message);

            var success = response.SuccessCount;
            _logger.LogInformation(
                "[FCM] type={Type} sent={Success}/{Total}",
                payload.Type, success, tokens.Count);

            // Cleanup invalidated tokens (FCM trả về errorCode "UNREGISTERED"
            // hoặc "INVALID_ARGUMENT" khi token bị xoá trên Firebase console
            // hoặc user gỡ app).
            await InvalidateFailedTokensAsync(tokens, response.Responses);
            return success;
        }
        catch (Exception ex)
        {
            // GAP-R6-RT-04 Fix: re-throw thay vì return 0 silently.
            // Trước đây: catch + return 0 → caller (RealOutboxPublisher) không biết FCM fail
            // → mark Processed → silent data loss. FCM push lỗi nhưng client không bao giờ nhận lại.
            // Sau: throw → caller bubble up → OutboxPublisherHostedService.MarkFailed → retry.
            _logger.LogError(ex, "[FCM] Send failed for type={Type} count={Count}",
                payload.Type, tokens.Count);
            throw;
        }
    }

    public async Task InvalidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var existing = await _deviceTokenRepository.GetByTokenAsync(token);
        if (existing == null || existing.IsInvalidated)
        {
            return;
        }
        existing.IsInvalidated = true;
        await _deviceTokenRepository.UpdateAsync(existing);
        await _deviceTokenRepository.SaveChangesAsync();
        _logger.LogInformation("[FCM] Invalidated device token id={TokenId}", existing.Id);
    }

    public async Task<int> SendAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default)
    {
        var payload = new PushNotificationPayload
        {
            Title = title,
            Body = body,
            Data = data ?? new Dictionary<string, string>()
        };
        return await SendToUsersAsync(new[] { userId }, payload);
    }

    private async Task EnsureFirebaseInitializedAsync()
    {
        if (_firebaseInitialized)
        {
            return;
        }
        await _initLock.WaitAsync();
        try
        {
            if (_firebaseInitialized)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(_settings.CredentialsJson))
            {
                throw new InvalidOperationException(
                    ApiErrorMessages.System.FirebaseCredentialsMissing);
            }
            var app = FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromJson(_settings.CredentialsJson)
            }, _settings.ProjectId);
            _firebaseInitialized = true;
            _logger.LogInformation("[FCM] Firebase Admin SDK initialized for project {ProjectId}",
                _settings.ProjectId);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InvalidateFailedTokensAsync(
        IReadOnlyList<BoardVerse.Core.Entities.DeviceToken> sentTokens,
        IReadOnlyList<SendResponse> responses)
    {
        var toInvalidate = new List<BoardVerse.Core.Entities.DeviceToken>();
        for (var i = 0; i < responses.Count; i++)
        {
            var resp = responses[i];
            // FirebaseAdmin 3.x: SendResponse không có IsSuccessful; check Exception != null.
            if (resp.Exception is not FirebaseMessagingException fmEx)
            {
                continue;
            }
            // FCM error code "UNREGISTERED" = user gỡ app hoặc token hết hạn.
            // "INVALID_ARGUMENT" = token format sai (rare).
            if (fmEx.MessagingErrorCode == MessagingErrorCode.Unregistered
                || fmEx.MessagingErrorCode == MessagingErrorCode.InvalidArgument
                || fmEx.ErrorCode == ErrorCode.NotFound)
            {
                var token = sentTokens[i];
                token.IsInvalidated = true;
                toInvalidate.Add(token);
            }
        }
        if (toInvalidate.Count == 0)
        {
            return;
        }
        foreach (var t in toInvalidate)
        {
            await _deviceTokenRepository.UpdateAsync(t);
        }
        await _deviceTokenRepository.SaveChangesAsync();
        _logger.LogInformation("[FCM] Marked {Count} tokens as invalidated", toInvalidate.Count);
    }

    public void Dispose()
    {
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
