# BoardVerse API Guide

This guide covers every API currently in the solution, how to call it, and what you get back on both happy and unhappy paths.

## Response Format

All API responses now use the same envelope:

```json
{
  "statusCode": 200,
  "message": "OK",
  "data": {},
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/login"
}
```

Notes:
- `data` can be `null` when an endpoint does not return a payload.
- `timestamp` is UTC.
- `path` is the request path.
- Error responses follow the same structure.

Common unhappy response examples:

```json
{
  "statusCode": 400,
  "message": "Validation failed",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/register"
}
```

```json
{
  "statusCode": 401,
  "message": "Unauthorized",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/protected/secret"
}
```

```json
{
  "statusCode": 403,
  "message": "Forbidden",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/usermanagement"
}
```

```json
{
  "statusCode": 404,
  "message": "Not found",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/usermanagement/guid"
}
```

```json
{
  "statusCode": 409,
  "message": "Conflict",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/userprofile"
}
```

```json
{
  "statusCode": 500,
  "message": "An unexpected error occurred.",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/debug/send-test-email"
}
```

Current unhappy cases by API:

- Auth APIs: `400`, `401`, `403`, `404`, `409`, `429`, and `500` depending on the action.
- Protected API: `401` for missing or invalid tokens.
- User Management API: `400`, `401`, `403`, and `404`.
- User Profile API: `401`, `404`, and `409`.
- Health APIs: usually `200`, but `GET /api/health/db-info` can return `500` if the database is unavailable.
- Debug Email API: `500` if SMTP fails.

## Base URL

Local:

```text
http://localhost:5022
```

Production example:

```text
https://your-render-url.onrender.com
```

## Authentication

Most protected endpoints require a Bearer token:

```http
Authorization: Bearer <your-jwt>
```

## 1. Auth APIs

Base route: `/api/auth`

### 1.1 Register

```http
POST /api/auth/register
```

Request body:

```json
{
  "username": "alice",
  "email": "alice@example.com",
  "phoneNumber": "0123456789",
  "password": "P@ssw0rd!"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Registration successful",
  "data": {
    "message": "Account created successfully. Please log in."
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/register"
}
```

Unhappy cases:
- Duplicate email or username returns `400`.
- Missing required fields returns validation `400`.
- Unexpected server failure returns `500`.

Example error:

```json
{
  "statusCode": 400,
  "message": "A user with the same username or email already exists.",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/register"
}
```

### 1.2 Login

```http
POST /api/auth/login
```

Request body:

```json
{
  "usernameOrEmail": "alice@example.com",
  "password": "P@ssw0rd!"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Login successful",
  "data": {
    "token": "jwt-token-here",
    "refreshToken": "refresh-token-here"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/login"
}
```

Unhappy cases:
- Wrong credentials returns `401`.
- Too many attempts returns `429`.
- Unexpected server failure returns `500`.

### 1.3 Google Login

```http
POST /api/auth/google-login
```

Request body:

```json
{
  "idToken": "google-id-token"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Google login successful",
  "data": {
    "token": "jwt-token-here",
    "refreshToken": "refresh-token-here"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/google-login"
}
```

Unhappy cases:
- Invalid Google token returns `401`.
- Missing Google email returns `401`.
- Unexpected token validation failure returns `500`.

### 1.4 Refresh Token

```http
POST /api/auth/refresh-token
```

Request body:

```json
{
  "refreshToken": "refresh-token-here"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Token refreshed successfully",
  "data": {
    "token": "new-jwt-token",
    "refreshToken": "new-refresh-token"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/refresh-token"
}
```

Unhappy cases:
- Missing or revoked refresh token returns `401`.
- Expired refresh token returns `401`.
- User not found for token returns `404`.

### 1.5 Logout

```http
POST /api/auth/logout
```

Request body:

```json
{
  "refreshToken": "refresh-token-here"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Logged out",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/logout"
}
```

Unhappy cases:
- Invalid token handling returns `500` if the flow fails unexpectedly.
- Missing/empty token may be rejected by validation.

### 1.6 Send Email Verification

> **⚠️ IMPORTANT: RENDER BLOCKS PORT 587 - PHẢI DÙNG SERVICE KHÁC NHƯ SENDGRID**
>
> Render blocks outbound SMTP connections to ports 25, 587, and 465. This endpoint will fail on Render with "Network is unreachable" error. You must use a transactional email service like SendGrid, Mailgun, or AWS SES instead of direct SMTP.

```http
POST /api/auth/send-email-verification
```

Request body:

```json
{
  "email": "alice@example.com"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Verification email sent.",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/send-email-verification"
}
```

Unhappy cases:
- Unknown email returns `404`.
- Email delivery failure returns `500`.

### 1.7 Verify Email

```http
POST /api/auth/verify-email
```

Request body:

```json
{
  "token": "123456"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Email verified",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/verify-email"
}
```

Unhappy cases:
- Invalid token returns `401`.
- Expired token returns `401`.

### 1.8 Request Password Reset

```http
POST /api/auth/request-password-reset
```

Request body:

```json
{
  "email": "alice@example.com"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Password reset email sent.",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/request-password-reset"
}
```

Unhappy cases:
- Unknown email returns `404`.
- Email not verified returns `403`.

### 1.9 Reset Password

```http
POST /api/auth/reset-password
```

Request body:

```json
{
  "token": "123456",
  "newPassword": "N3wP@ssw0rd!"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Password has been reset",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/reset-password"
}
```

Unhappy cases:
- Invalid reset token returns `401`.
- Expired reset token returns `401`.

### 1.10 Change Password

```http
POST /api/auth/change-password
```

Requires Bearer token.

Request body:

```json
{
  "currentPassword": "OldP@ssw0rd!",
  "newPassword": "N3wP@ssw0rd!"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Password has been changed",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/change-password"
}
```

Unhappy cases:
- Missing or invalid JWT returns `401`.
- Wrong current password returns `401`.
- Same new password returns `400`.

### 1.11 Link Google Account

```http
POST /api/auth/link-google
```

Request body:

```json
{
  "idToken": "google-id-token"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Google account linked successfully",
  "data": {
    "token": "jwt-token-here",
    "refreshToken": "refresh-token-here"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/link-google"
}
```

Unhappy cases:
- No matching local account returns `404`.
- Invalid Google token returns `401`.

## 2. Health APIs

Base route: `/api/health`

### 2.1 Status

```http
GET /api/health/status
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "API is operational",
  "data": {
    "status": "healthy"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/health/status"
}
```

Unhappy cases:
- Unexpected failure returns `500`.

### 2.2 Database Info

```http
GET /api/health/db-info
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Database connected",
  "data": {
    "status": "connected",
    "userCount": 12
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/health/db-info"
}
```

Unhappy cases:
- Database read failure returns `400` or `500` depending on the source of the failure.

### 2.3 Ping

```http
GET /api/health/ping
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "pong",
  "data": {
    "message": "pong"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/health/ping"
}
```

## 3. Debug Email API

Base route: `/api/debug`

> This is a debug endpoint. Treat it as development-only unless you intentionally keep it enabled.

### 3.1 Send Test Email

> **⚠️ IMPORTANT: RENDER BLOCKS PORT 587 - PHẢI DÙNG SERVICE KHÁC NHƯ SENDGRID**
>
> Render blocks outbound SMTP connections to ports 25, 587, and 465. This endpoint will fail on Render with "Network is unreachable" error. You must use a transactional email service like SendGrid, Mailgun, or AWS SES instead of direct SMTP.

```http
POST /api/debug/send-test-email
```

Request body:

```json
{
  "to": "recipient@example.com",
  "subject": "Hello from BoardVerse",
  "body": "This is a test email."
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Email sent (or queued).",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/debug/send-test-email"
}
```

Unhappy cases:
- SMTP failure returns `500`.
- Invalid recipient/body can fail validation or downstream email sending.

## 4. Protected API

Base route: `/api/protected`

### 4.1 Secret

```http
GET /api/protected/secret
```

Requires Bearer token.

Happy case:

```json
{
  "statusCode": 200,
  "message": "You have accessed a protected endpoint.",
  "data": {
    "userId": "guid-here",
    "email": "alice@example.com"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/protected/secret"
}
```

Unhappy cases:
- Missing token returns `401`.
- Invalid token returns `401`.

## 5. User Management API

Base route: `/api/usermanagement`

> Requires admin role.

### 5.1 Get All Users

```http
GET /api/usermanagement
```

Query parameters:

- `search` filters by username or email.
- `role` filters by role name.
- `isActive` filters by active state.
- `page` controls the page number.
- `pageSize` controls how many records are returned per page.

Example:

```http
GET /api/usermanagement?search=alice&role=User&isActive=true&page=1&pageSize=10
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Users retrieved successfully",
  "data": [
    {
      "id": "guid",
      "username": "alice",
      "email": "alice@example.com",
      "role": "User",
      "isActive": true,
      "createdAt": "2026-05-31T12:34:56Z"
    }
  ],
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/usermanagement"
}
```

Unhappy cases:
- Missing token returns `401`.
- Non-admin token returns `403`.

### 5.2 Get User By Id

```http
GET /api/usermanagement/{id}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "User retrieved successfully",
  "data": {
    "id": "guid",
    "username": "alice",
    "email": "alice@example.com",
    "role": "User",
    "isActive": true,
    "createdAt": "2026-05-31T12:34:56Z"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/usermanagement/guid"
}
```

Unhappy cases:
- Unknown id returns `404`.

### 5.3 Create User

```http
POST /api/usermanagement
```

Request body:

```json
{
  "username": "bob",
  "email": "bob@example.com",
  "password": "P@ssw0rd!",
  "role": "User"
}
```

Happy case:

```json
{
  "statusCode": 201,
  "message": "User created",
  "data": {
    "id": "guid",
    "username": "bob",
    "email": "bob@example.com",
    "role": "User"
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/usermanagement"
}
```

Unhappy cases:
- Duplicate email or username returns `400`.

### 5.4 Update User

```http
PUT /api/usermanagement/{id}
```

Request body:

```json
{
  "username": "bobby",
  "email": "bobby@example.com",
  "role": "Admin",
  "isActive": true,
  "password": "NewP@ssw0rd!"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "User updated successfully",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/usermanagement/guid"
}
```

Unhappy cases:
- Unknown id returns `404`.

### 5.5 Disable User

```http
DELETE /api/usermanagement/{id}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "User disabled successfully",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/usermanagement/guid"
}
```

Unhappy cases:
- Unknown id returns `404`.

## 6. User Profile API

Base route: `/api/userprofile`

> Requires Bearer token.

### 6.1 Get My Profile

```http
GET /api/userprofile
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Profile retrieved successfully",
  "data": {
    "userId": "guid",
    "username": "alice",
    "gamerTag": "AceAlice",
    "bio": "Board game enthusiast",
    "globalElo": 1200,
    "level": 1
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/userprofile"
}
```

Unhappy cases:
- Missing token returns `401`.
- Invalid token returns `401`.
- Missing user claim returns `401`.

### 6.2 Create Profile

```http
POST /api/userprofile
```

Request body:

```json
{
  "gamerTag": "AceAlice",
  "bio": "Board game enthusiast",
  "firstName": "Alice",
  "lastName": "Nguyen",
  "dateOfBirth": "1998-01-01",
  "homeAddress": "HCMC"
}
```

Happy case:

```json
{
  "statusCode": 201,
  "message": "Profile created",
  "data": {
    "userId": "guid",
    "username": "alice",
    "gamerTag": "AceAlice",
    "bio": "Board game enthusiast",
    "globalElo": 1200,
    "level": 1
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/userprofile"
}
```

Unhappy cases:
- Existing active profile returns `409`.
- Missing user returns `404`.

### 6.3 Update Profile

```http
PUT /api/userprofile
```

Request body:

```json
{
  "gamerTag": "AceAlice2",
  "bio": "Updated bio"
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Profile updated successfully",
  "data": {
    "userId": "guid",
    "username": "alice",
    "gamerTag": "AceAlice2",
    "bio": "Updated bio",
    "globalElo": 1200,
    "level": 1
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/userprofile"
}
```

Unhappy cases:
- Missing user returns `404`.

### 6.4 Update Progress

```http
POST /api/userprofile/progress
```

Request body:

```json
{
  "globalElo": 1350,
  "level": 5
}
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Profile progress updated successfully",
  "data": {
    "userId": "guid",
    "username": "alice",
    "gamerTag": "AceAlice2",
    "bio": "Updated bio",
    "globalElo": 1350,
    "level": 5
  },
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/userprofile/progress"
}
```

Unhappy cases:
- Missing user returns `404`.

### 6.5 Delete Profile

```http
DELETE /api/userprofile
```

Happy case:

```json
{
  "statusCode": 200,
  "message": "Profile deleted successfully",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/userprofile"
}
```

Unhappy cases:
- Missing token or claim returns `401`.

## 7. Quick cURL Examples

Login:

```bash
curl -X POST http://localhost:5022/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"usernameOrEmail\":\"alice@example.com\",\"password\":\"P@ssw0rd!\"}"
```

Call a protected endpoint:

```bash
curl http://localhost:5022/api/protected/secret \
  -H "Authorization: Bearer <your-jwt>"
```

Create a profile:

```bash
curl -X POST http://localhost:5022/api/userprofile \
  -H "Authorization: Bearer <your-jwt>" \
  -H "Content-Type: application/json" \
  -d "{\"gamerTag\":\"AceAlice\"}"
```

## 8. Error Handling Rules

When an API fails, the response still uses the same envelope.

Example:

```json
{
  "statusCode": 401,
  "message": "Invalid credentials.",
  "data": null,
  "timestamp": "2026-05-31T12:34:56Z",
  "path": "/api/auth/login"
}
```

If you add a new endpoint, keep the same pattern:
- inherit from `BaseApiController`
- return `IActionResult`
- call `NewResponse(statusCode, message, data)`
- let `AppException` flow to middleware for consistent error output
