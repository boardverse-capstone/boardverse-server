# 1.1 System Architecture

## 1.1.1 Overall System Architecture Diagram

The following diagram presents the overall system architecture of **BoardVerse** — a board game center management platform that combines online matchmaking, offline POS operation, and a payment gateway. The system is decomposed into four major sub-systems (Player-facing, Cafe/Manager-facing, Admin-facing, and **BoardVerse Backend Core**), each interacting with four external systems (PostgreSQL/Neon, Redis, Brevo, Google OAuth, BoardGameGeek, and SePay).

```
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│                                    CLIENT APPLICATIONS                                    │
├────────────────────────────┬─────────────────────────────┬───────────────────────────────┤
│  Player Mobile App (iOS/   │  Cafe POS Dashboard (Web)    │  Admin Web Portal             │
│  Android)                   │  + Cafe Partner Portal (Web) │  (Browser)                    │
│                             │                              │                               │
│  • Auth, Profile            │  • Cafe Profile / Inventory  │  • User Moderation            │
│  • Lobby / Match            │  • POS / Active Session      │  • Master Catalog             │
│  • Tournament / Booking     │  • Staff / Settlement        │  • Cafe Operational           │
│  • Friend / Karma           │  • Tournament POS            │  • SePay Account Mgmt         │
│  • Deposit / Payment        │  • SePay Account Linking     │  • System Configuration       │
└───────────────┬─────────────┴──────────────┬──────────────┴────────────┬──────────────────┘
                │ HTTPS / REST + JSON        │ HTTPS / REST + JSON        │ HTTPS / REST + JSON
                │ + SignalR (Lobby/Invite)    │ + SignalR (POS)            │
                ▼                             ▼                           ▼
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│                       BOARDVERSE BACKEND CORE (ASP.NET Core 8 Web API)                    │
│                                     i:\Coding\SEP490\BE\boardverse-server                │
├──────────────────────────────────────────────────────────────────────────────────────────┤
│  PRESENTATION LAYER                          APPLICATION LERSERVICE LAYER                │
│  ──────────────────                          ─────────────────────────────                  │
│  • 36 API Controllers                        • AuthService, UserService                   │
│  • LobbyHub (SignalR)                        • CafeService, CafePosService                 │
│  • Swagger / Swagger UI                      • LobbyService, BookingService                │
│  • ApiExceptionMiddleware                    • TournamentService, MatchService             │
│  • JWT Bearer Authentication                 • PaymentService, SePayAccountService         │
│  • Model Validation Filter                   • FriendService, KarmaRatingService           │
│  • CORS / Static Files                       • 10+ Background Jobs (Hosted Service)        │
│  • Newtonsoft + System.Text.Json              • LobbyTimeoutJob, BookingDepositExpiryJob    │
│                                              • TournamentReminderJob, KarmaWindowJob       │
├──────────────────────────────────────────────────────────────────────────────────────────┤
│  DOMAIN LAYER                                INFRASTRUCTURE LAYER                        │
│  ──────────────                              ──────────────────────                        │
│  • Entities (User, Cafe, Lobby, Booking,     • BoardVerseDbContext (EF Core)               │
│    Match, Tournament, TournamentMatch,       • Repositories (50+ Tables)                  │
│    BookingDeposit, ActiveSession, ...)       • Npgsql + PostGIS                            │
│  • DTOs (Request/Response)                   • Entity Type Configurations                  │
│  • Enums (BookingStatus, BookingDepositStatus│ • Migrations (schema versioning)             │
│    LobbyStatus, TournamentStatus, ...)       • HttpClient factory for 3rd-party APIs       │
│  • Custom Exceptions                         • StackExchange.Redis (cache)                 │
│  • Helpers (Karma, Settlement, GeoLocation)  • BCrypt (password hashing)                  │
└────────────────────────────────────┬─────────────────────────────────────────────────────┘
                                     │
                                     │ TCP / Npgsql / SSL
                                     ▼
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│                              EXTERNAL / 3RD-PARTY SYSTEMS                                │
├────────────────────────────┬─────────────────────────────┬───────────────────────────────┤
│  DATABASE                  │  PAYMENT & AUTH             │  COMMUNICATION & DATA          │
│  ────────────              │  ─────────────────          │  ────────────────────────      │
│  • Neon PostgreSQL         │  • SePay Payment Gateway    │  • Brevo (Email) Transactional │
│    (Serverless Postgres)   │   - pgapi.sepay.vn checkout │  • Google OAuth 2.0 (SSO)      │
│  • PostGIS extension       │   - Webhook receiver        │  • BoardGameGeek (BGG) XML API │
│  • Redis (Distributed      │  • VietQR (fallback dev)    │  • Render.com (Hosting)        │
│    Cache) optional         │  • JWT (HS256, self-signed) │  • Cloudflare (DNS/CDN)        │
│  • Fallback: In-Memory     │  • BCrypt (password hash)   │                               │
└────────────────────────────┴─────────────────────────────┴───────────────────────────────┘
```

---

## 1.1.2 Explanation of Diagram Components

### A. Client Applications

The system serves three distinct user groups, each with its own client application optimized for the relevant workflow.

| Component | Tech | Users | Capabilities |
|-----------|------|-------|--------------|
| **Player Mobile App** | Native iOS/Android (Flutter / React Native) | `Player` role | Discovery (geo, categories), matchmaking (`Lobby`), booking, deposits, friend/social, tournament registration, ratings, push notifications. Connects to `/hubs/lobby` SignalR endpoint for real-time lobby updates. |
| **Cafe POS Dashboard** | Responsive Web (SPA) | `Manager`, `CafeStaff` | Operational profile, inventory, table management, POS session start/end, QR check-in, settlement. |
| **Cafe Partner Portal** | Responsive Web (SPA) | `Manager` (cafe owner) | Cafe onboarding application, manager account, profile, staff invitation. |
| **Admin Web Portal** | Responsive Web (SPA) | `Admin` role | User moderation, master catalog, cafe operational status, SePay account linking, system-wide configuration. |

All three clients communicate **exclusively over HTTPS REST + JSON** with the central backend. The Player app additionally uses **SignalR** (`/hubs/lobby`) for low-latency lobby events.

---

### B. BoardVerse Backend Core (ASP.NET Core 8 Web API)

The backend is structured as a **Clean Architecture-style** solution with four layers:

#### B.1. Presentation Layer (`BoardVerse.API`)

Hosts the HTTP entry points. The layer is composed of:

- **36 Controllers** (`/Controllers/*.cs`) — one per domain area (Auth, User, Cafe, Lobby, Booking, Match, Tournament, Payment, Friend, Admin, Health, …). Each controller inherits from `BaseApiController` and exposes unified envelope responses via `NewResponse(statusCode, message, data)`.
- **LobbyHub** — SignalR hub for bi-directional real-time lobby communication (player join/leave, status changes, chat, invitations).
- **Cross-cutting middleware:**
  - `ApiExceptionMiddleware` — converts typed exceptions (`NotFoundException`, `ForbiddenException`, `ConflictException`, `BadRequestException`) into the standard envelope → `400/403/404/409/500`.
  - `JwtBearer` authentication. Issued tokens are HS256-signed with `JwtSettings:SecurityKey`, validated against `ValidIssuer`/`ValidAudience`, lifetime enforced.
  - CORS policy `AllowAll` (configurable for production).
  - Swagger UI for interactive API documentation at `/swagger`.
  - `ValidateModelAttribute` — runs FluentValidation / DataAnnotation checks before action execution.

#### B.2. Application / Service Layer (`BoardVerse.Services`)

Contains **business logic** split into ~30 services, each paired with an `IXxxService` interface registered in the IoC container:

- **Domain services:** `AuthService`, `UserProfileService`, `UserManagementService`, `CafeService`, `CafeInventoryService`, `CafePosService`, `LobbyService`, `BookingService`, `TournamentService`, `MatchService`, `FriendService`, `KarmaRatingService`, `SettlementService`, `PaymentService`, `BggService`, …
- **Cross-cutting services:** `RedisServiceExtensions`, `BrevoService`, `BggService`, `SePayClient`, `SystemConfigurationService`.
- **10+ background jobs** (`IHostedService`) that run inside the SAME process:
  - `LobbyTimeoutJob` — expires lobbies that never reach `Full`.
  - `BookingDepositExpiryJob` — marks orphan deposits as `Expired`.
  - `KarmaWindowJob` — closes 24-hour karma rating windows.
  - `SettlementRetryJob` — retries failed deposit transfers to cafe managers.
  - `TournamentExpiryJob`, `TournamentReminderJob`, `TournamentNoShowDetectionJob`.
  - `LobbyCleanupJob`, `FriendRequestExpiryJob`.

> These background jobs share the same DI container and `DbContext` lifetime as the HTTP request pipeline, but are **disabled in the `Testing` environment** (xUnit integration tests) to avoid side effects.

#### B.3. Domain Layer (`BoardVerse.Core`)

Pure C# domain model with **no external dependencies**:

- **Entities** — POCO classes mapped to DB tables (e.g. `User`, `Cafe`, `Lobby`, `Booking`, `BookingDeposit`, `Tournament`, `TournamentMatch`, `ActiveSession`, `KarmaRating`, `MatchResult`, `CafeSettlement`, …).
- **DTOs** — `RequestDto` / `ResponseDto` per API; documented with XML comments.
- **Enums** — `BookingStatus`, `BookingDepositStatus`, `LobbyStatus`, `TournamentStatus`, `TournamentMatchStatus`, `ActiveSessionStatus`, `CafeTableStatus`, …
- **Custom Exceptions** — `NotFoundException`, `ForbiddenException`, `ConflictException`, `BadRequestException`, `ValidationException`.
- **Helpers** — `KarmaHelper`, `SettlementHelper`, `GeoLocationHelper`, `CafeTableSyncHelper`, `ApiErrorMessages`, `JwtSettings`, `BrevoSettings`, `SePaySettings`.

#### B.4. Infrastructure Layer (`BoardVerse.Data`)

- **`BoardVerseDbContext`** — EF Core 8 context that opens connections to Neon PostgreSQL using Npgsql.
- **70+ repository classes** — one per entity; each implements `IXxxRepository` and is registered as `Scoped` in DI.
- **Entity Type Configurations** (`IEntityTypeConfiguration<T>`) — fluent mappings for tables, columns, FKs, indexes (e.g. `BookingConfiguration`, `LobbyConfiguration`, `TournamentConfiguration`).
- **Migrations** — EF Core migration history for schema evolution.
- **Database connectivity** — `Npgsql` driver + `NetTopologySuite` for PostGIS geo-queries (find nearby cafes).
- **HTTP clients** — typed `HttpClient` for Google, Brevo, BGG, SePay.

---

### C. External / 3rd-Party Systems

| Sub-system | Purpose | Where Used |
|------------|---------|------------|
| **Neon PostgreSQL** (Serverless Postgres on AWS) | Primary OLTP database. Stores all 50+ tables. Hosted on `*.aws.neon.tech`. Supports PostGIS extension for geo queries. | Repository layer (Npgsql + EF Core) |
| **PostGIS** | Spatial extension of PostgreSQL — stores `geography` points for cafe locations and powers `ST_DWithin` queries (cafes near me). | `ICafeRepository.GetNearbyAsync` |
| **Redis** (optional) | Distributed cache for login throttling, system configuration cache, lobby state. Falls back to in-memory cache when `REDIS_URL` is not set. | `RedisServiceExtensions` |
| **SePay Payment Gateway** | QR payment gateway for booking deposits and session payments. Endpoints: `https://pgapi.sepay.vn/v1/checkout/init` (cafe merchant) and `https://pay.sepay.vn/v1/checkout/init` (BoardVerse central). Webhook receiver at `POST /api/payments/sepay/webhook`. HMAC-SHA256 signature verification. | `SePayClient`, `SePayWebhookController`, `SePayAccountController` |
| **VietQR** (fallback) | QR generator for dev/CI when SePay is not configured. | `PaymentService` |
| **Brevo** (formerly Sendinblue) | Transactional email (email verification, password reset, cafe approval notifications, manager account creation). | `BrevoEmailService` |
| **Google OAuth 2.0** | Social login and account linking. Validates Google ID token via `Google.Apis.Auth`. | `AuthService` |
| **BoardGameGeek (BGG) XML API** | Game catalog import metadata (admin tool). Polled with retry/backoff. | `BggApiClient` |
| **JWT** (self-signed) | Stateless access tokens. Issued by `AuthService`, validated by `JwtBearer` middleware. | `AuthService`, `JwtBearerEventHandlers` |
| **BCrypt** | Password hashing library (no external service). | `AuthService.RegisterAsync` |
| **Render.com** | Production hosting target. Reads `PORT` env var for binding. | Deployment |
| **Cloudflare** | DNS + CDN in front of the Render-hosted API. | Deployment |

---

## 1.1.3 Inter-Component Relationships and Data Flow

The diagram is organized around **four primary data flows** that show how the components interact in real-world scenarios.

### Flow 1 — Player onboarding and matchmaking

```
Player Mobile App
       │
       │ 1. POST /api/auth/login  (email + password | Google ID token)
       ▼
AuthController ──► AuthService ──► UserRepository ──► Neon PostgreSQL
                          │
                          │ 2. JWT issued (HS256 signed)
                          ▼
                  Player Mobile App
                          │
                          │ 3. GET /api/cafes?lat=…&lng=…&radiusKm=10
                          ▼
                  CafeController ──► CafeRepository ──► PostGIS ST_DWithin
                          │
                          │ 4. POST /api/lobbies  (Lobby.Status = Open)
                          ▼
                  LobbyController ──► LobbyService ──► LobbyRepository
                          │
                          │ 5. Real-time join/leave via SignalR
                          ▼
                       LobbyHub ◄──── Player Mobile App
```

### Flow 2 — Booking + SePay deposit

```
Player Mobile App
       │
       │ 1. Lobby.Full reached → Host calls POST /api/bookings
       ▼
BookingController ──► BookingService ──► BookingRepository
       │
       │    (CafeTableId conflict check, LobbyId FK, QR auto-generated)
       ▼
   Database: Bookings row (Status = PendingDeposit)
       │
       │ 2. POST /api/payments/booking-deposit
       ▼
PaymentController ──► PaymentService ──► SePayClient
       │
       │    SePayClient calls https://pgapi.sepay.vn/v1/checkout/init
       ▼
       SePay Gateway
       │
       │ 3. Webhook  POST /api/payments/sepay/webhook
       │    (HMAC-SHA256 verified by SePayWebhookController)
       ▼
SePayWebhookController ──► PaymentService.ConfirmDepositAsync
       │
       │    Updates Booking.Status = Confirmed
       │    Creates BookingDeposit (Status = Paid)
       ▼
       Neon PostgreSQL
```

### Flow 3 — Cafe POS session

```
Cafe Staff scans QR code at counter
       │
       │ 1. POST /api/active-sessions  (StartSession)
       ▼
ActiveSessionController ──► CafePosService
       │
       │    Validates Booking is Confirmed, marks CheckedIn
       │    Reads lobby members → invites them into ActiveSession
       ▼
       Database: ActiveSession row linked to Booking
       │
       │ 2. Add games to session, record damage, end session
       ▼
CafePosController ──► CafePosService
       │
       │ 3. POST /api/payments/session-payment
       ▼
PaymentController ──► SePayClient (Cafe merchant, not BoardVerse)
       │
       │    Settles via cafe's own SePay account
       ▼
       Database: Invoice + Payment populated
```

### Flow 4 — Cafe manager onboarding

```
Anonymous Browser
       │
       │ 1. POST /api/cafe-partner-applications (Phase 1: register)
       ▼
CafePartnerApplicationController ──► CafePartnerApplicationService
       │
       │    BrevoService.SendAsync → confirmation email
       ▼
       Database: CafePartnerApplication row (Status = PendingReview)
       │
       │ 2. Admin reviews → POST /api/admin/cafe-partner-applications/{id}/approve
       ▼
AdminCafePartnerApplicationController ──► AdminModerationService
       │
       │    Creates Cafe, generates Manager user, sends email with credentials
       ▼
       Database: Cafe + User (Manager role) + CafeStaff
       │
       │ 3. Manager logs in → ManagerController (Profile, Inventory, SePay linking)
       ▼
       Cafe POS Dashboard
```

---

## 1.1.4 Architectural Patterns and Principles

| Pattern / Principle | Where Applied |
|---------------------|---------------|
| **Clean Architecture** | `BoardVerse.Core` (domain) ← `BoardVerse.Data` (infra) ← `BoardVerse.Services` (app) ← `BoardVerse.API` (presentation) |
| **Repository Pattern** | One repository per entity, registered `Scoped` in DI |
| **Dependency Injection** | All services, repositories, and HTTP clients registered via `Program.cs` extension methods |
| **Strategy / Provider** | `ISystemConfigurationProvider` chooses between DB-stored config and `appsettings.json` |
| **Background Jobs (Hosted Service)** | 10+ jobs for time-sensitive processes (timeout, expiry, retry) |
| **Webhook Receiver** | `SePayWebhookController` accepts external callbacks with HMAC verification |
| **Soft Delete** | `IsActive` flags on `Cafe`, `CafeTable`, `GameTemplate` instead of `DELETE` |
| **Pre-signed QR Codes** | `VerificationQRCode` generated at create time, checked at POS |
| **State Machine Enforcement** | `BookingStatus`, `LobbyStatus`, `TournamentStatus`, `TournamentMatchStatus` enforced in service layer |
| **Unified Response Envelope** | Every API returns `{ statusCode, isSuccess, message, data }` |
| **Centralized Error Handling** | `ApiExceptionMiddleware` + `ApiErrorMessages` (operation-specific, i18n-ready) |
| **Multi-Environment Configuration** | `appsettings.json`, `appsettings.Development.json`, plus env vars (`DATABASE_URL`, `REDIS_URL`, `PORT`, `ENABLE_SWAGGER`) |

---

## 1.1.5 Deployment Topology

```
[ Player App ]  ─── HTTPS ───►  [ Cloudflare CDN ]
                                          │
                                          ▼
[ Cafe POS Web ] ─── HTTPS ───►  [ Render.com (API) ]
                                          │
                                          ▼
                                  [ BoardVerse API ]
                                  /        |        \
                                 /         |         \
                  [ Neon PostgreSQL ]   [ Redis ]   [ Brevo / SePay /
                   (main + branch)     (optional)    BGG / Google ]
```

- **Backend** is hosted on **Render.com** as a single Web Service. The `PORT` env var controls binding.
- **Database** is **Neon PostgreSQL** (serverless Postgres on AWS, ap-southeast-1). Two branches are used: `testing` and `production`. The `appsettings.json` connection string targets the production branch; tests use environment variables.
- **Redis** is optional. When `REDIS_URL` is set, the backend uses `StackExchange.Redis`; otherwise it falls back to `IDistributedMemoryCache` for local development.
- **Stateless API** — JWT tokens carry user identity; no server-side session. Horizontally scalable by scaling the Render service up.

---

## 1.1.6 Summary

BoardVerse is a **monolithic, Clean-Architecture, multi-layered ASP.NET Core 8 Web API** that:

1. Serves **three client types** (Player Mobile, Cafe POS, Admin Web) via a unified REST + JSON API.
2. Uses **SignalR** for real-time lobby communication.
3. Stores all data in **Neon PostgreSQL** with PostGIS for geo-queries.
4. Integrates with **six external systems** (Google, SePay, VietQR, Brevo, BGG, Render hosting) but none of them is a hard architectural dependency — each is encapsulated behind a service interface and can be swapped or stubbed in tests.
5. Runs **time-sensitive domain logic** inside the same process via `IHostedService` background jobs, keeping the synchronous request path lean.
6. Enforces **domain invariants** (state machines, ownership rules, FK constraints) in the service layer, making the system safe to evolve from a single-process monolith into modular services later if needed.
