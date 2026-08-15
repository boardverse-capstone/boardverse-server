# SignalR Hubs — Authentication Contract

> Tài liệu này quy định cách client xác thực khi kết nối tới SignalR hubs của BoardVerse.
> Áp dụng cho: `/hubs/lobby`, `/hubs/pos`.

---

## I. VẤN ĐỀ

SignalR sử dụng WebSocket (hoặc Long Polling / Server-Sent Events fallback) cho kết nối
real-time. **WebSocket handshake không cho phép client gắn custom HTTP header** (đây là
giới hạn của browser WebSocket API), nên cách thông dụng là truyền JWT qua **query string
`access_token`** thay vì `Authorization: Bearer ...`.

Tuy nhiên `JwtBearerHandler` mặc định của ASP.NET Core chỉ đọc từ header, không tự động
đọc từ query string. Nếu không xử lý riêng, mọi negotiate request từ client sẽ bị 401 với
message `"Bạn cần đăng nhập để thực hiện thao tác này. Vui lòng đăng nhập trước nhé."`.

## II. GIẢI PHÁP

`Program.cs` đã wire `OnMessageReceived` cho `JwtBearerEvents` để lift `access_token` query
string vào bearer pipeline **chỉ** cho path `/hubs/*`:

```csharp
options.Events.OnMessageReceived = context =>
{
    var path = context.HttpContext.Request.Path;
    if (!path.StartsWithSegments("/hubs")) return Task.CompletedTask;

    var accessToken = context.Request.Query["access_token"];
    if (string.IsNullOrEmpty(accessToken)) return Task.CompletedTask;

    var token = accessToken.ToString();
    var parts = token.Split('.');
    if (parts.Length != 3) return Task.CompletedTask; // JWT phải có 3 phần

    context.Token = token;
    return Task.CompletedTask;
};
```

Sau bước này, toàn bộ pipeline JWT (signature, issuer, audience, lifetime, role claims)
chạy bình thường, kể cả `OnTokenValidated` callback (`JwtBearerEventHandlers.cs`) verify
user tồn tại và không bị khóa.

## III. CONTRACT CHO CLIENT

### III.1. Kết nối với token hợp lệ

```ts
// @microsoft/signalr (browser / React / Vue / Angular)
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/pos", {
    accessTokenFactory: () => localStorage.getItem("accessToken") ?? "",
  })
  .withAutomaticReconnect()
  .build();

await connection.start();
```

```dart
// signalr_netcore (Flutter / Dart)
final hub = HubConnectionBuilder()
    .withUrl("${baseUrl}/hubs/pos",
        options: HttpConnectionOptions(
          accessTokenFactory: () async => token,
        ))
    .build();

await hub.start();
```

Client **KHÔNG** cần gắn `Authorization` header — chỉ cần truyền qua `accessTokenFactory`.

### III.2. URL negotiate

```
POST /hubs/pos/negotiate?access_token=eyJhbGciOiJIUzI1NiIs...
POST /hubs/lobby/negotiate?access_token=eyJhbGciOiJIUzI1NiIs...
```

| Status | Ý nghĩa |
|---|---|
| `200` | Token hợp lệ, trả về connection token + connection ID. |
| `401` (AuthorizationHeaderMissing) | Không có header `Authorization` lẫn query `access_token`. Client quên đăng nhập. |
| `401` (TokenInvalidSignature / TokenExpired / UserNoLongerExists) | Token sai chữ ký, hết hạn, hoặc user đã bị xóa. Client phải refresh token hoặc đăng nhập lại. |
| `403` | Token hợp lệ nhưng user bị `restricted` / `suspended` / `banned` (BR-RISK-04). |

### III.3. Token không hợp lệ (chuỗi rác)

Nếu `access_token` query không phải JWT 3 phần (`header.payload.signature`), handler bỏ
qua — request đi tiếp với `context.Token = null` → pipeline JWT reject → 401.

Điều này đảm bảo query string rác không làm hỏng handler, và không bypass được security.

## IV. VÌ SAO CHỈ ÁP DỤNG CHO `/hubs/*`?

REST endpoint vẫn dùng `Authorization: Bearer ...` header như bình thường — đó là best
practice cho HTTP API (token không bị log trong access log, không lưu vào browser history).

Chỉ mở rộng query-string support cho hub path, vì:
1. Đây là điểm hạn chế kỹ thuật của WebSocket transport (SignalR), không phải design choice.
2. Hub endpoints không log body/query nên rủi ro token leak qua log bằng 0 (so với query string của GET API vốn có thể xuất hiện trong IIS/Nginx access log).
3. CORS `SignalRCors` policy vẫn bắt buộc `AllowCredentials()` + trusted origins.

## V. CHECKLIST KHI REVIEW HUB LIÊN QUAN

1. Hub mới map qua `app.MapHub<X>(path).RequireCors("SignalRCors")`.
2. Hub class có `[Authorize]` ở mức class hoặc method cần auth.
3. Hub method **không** gọi trực tiếp `_lobbyRepository` / `_activeSessionRepository` với userId từ query — phải dùng `Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value` để tránh IDOR.
4. Subscribe method (`JoinSession`, `JoinLobby`, `JoinUserNotifications`) **luôn** verify user là participant trước khi `Groups.AddToGroupAsync` (xem PosHub.cs / LobbyHub.cs hiện tại).
5. Nếu sau này thêm hub path mới, đảm bảo path bắt đầu bằng `/hubs` để `OnMessageReceived` bắt được.

## VI. THAM CHIẢU

- Source: `BoardVerse.API/Program.cs` (JwtBearer wiring).
- Hubs: `BoardVerse.API/Hubs/PosHub.cs`, `LobbyHub.cs`.
- JWT handler: `BoardVerse.API/Authentication/JwtBearerEventHandlers.cs`.
- Tests: `BoardVerse.Tests/Integration/SignalRHubAuthIntegrationTests.cs`.
- BR liên quan: BR-RISK-04 (account status check tại `OnTokenValidated`).