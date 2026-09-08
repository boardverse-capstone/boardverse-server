# Encoding Scan Report

- Generated: 2026-09-07 21:47:38
- Root: `c:\Users\ASUS\source\repos\BoardVerse`
- Total .cs files scanned: 978

## Summary

| Encoding | Count | Action |
|----------|------:|--------|
| UTF-8 with BOM | 8 | No action (canonical) |
| UTF-8 without BOM | 970 | Recommend ADD BOM for VS/Rider compatibility |
| NON-UTF-8 (Windows-1252 / Latin-1) | 0 | **REQUIRED**: convert to UTF-8 + add BOM |

## Files with UTF-8 without BOM (optional: add BOM)

| Path | Size (bytes) |
|------|-------------:|
| `ApplyMigration\Program.cs` | 592 |
| `BoardVerse.API\Authentication\JwtAuthFailureContext.cs` | 1244 |
| `BoardVerse.API\Authentication\JwtBearerEventHandlers.cs` | 5969 |
| `BoardVerse.API\BackgroundServices\AlertExpiryCleanupJob.cs` | 2332 |
| `BoardVerse.API\BackgroundServices\AutoReleaseExpiredSessionsJob.cs` | 12721 |
| `BoardVerse.API\BackgroundServices\BookingDepositExpiryJob.cs` | 2034 |
| `BoardVerse.API\BackgroundServices\BvcTopUpExpiryJob.cs` | 2192 |
| `BoardVerse.API\BackgroundServices\CoolingOffJob.cs` | 4420 |
| `BoardVerse.API\BackgroundServices\DeviceTokenCleanupJob.cs` | 3377 |
| `BoardVerse.API\BackgroundServices\FriendRequestExpiryJob.cs` | 1992 |
| `BoardVerse.API\BackgroundServices\KarmaWindowExpiryJob.cs` | 3773 |
| `BoardVerse.API\BackgroundServices\KarmaWindowJob.cs` | 2537 |
| `BoardVerse.API\BackgroundServices\LegacyBookingCleanupJob.cs` | 2607 |
| `BoardVerse.API\BackgroundServices\LobbyAtRiskWarningJob.cs` | 7648 |
| `BoardVerse.API\BackgroundServices\LobbyCleanupJob.cs` | 5145 |
| `BoardVerse.API\BackgroundServices\LobbyInviteExpiryJob.cs` | 3390 |
| `BoardVerse.API\BackgroundServices\LobbyNotificationJob.cs` | 10382 |
| `BoardVerse.API\BackgroundServices\LobbyTimeoutJob.cs` | 12232 |
| `BoardVerse.API\BackgroundServices\OutboxCleanupJob.cs` | 2328 |
| `BoardVerse.API\BackgroundServices\ReservationDeadlineJob.cs` | 4613 |
| `BoardVerse.API\BackgroundServices\ReservationNoShowDetectionJob.cs` | 9550 |
| `BoardVerse.API\BackgroundServices\RiskScoreRecomputeJob.cs` | 1974 |
| `BoardVerse.API\BackgroundServices\SessionExtensionRequestExpiryJob.cs` | 3998 |
| `BoardVerse.API\BackgroundServices\SettlementRetryJob.cs` | 6100 |
| `BoardVerse.API\BackgroundServices\SuspensionExpiryCheckJob.cs` | 4058 |
| `BoardVerse.API\BackgroundServices\TournamentExpiryJob.cs` | 2675 |
| `BoardVerse.API\BackgroundServices\TournamentNoShowDetectionJob.cs` | 2853 |
| `BoardVerse.API\BackgroundServices\TournamentReminderJob.cs` | 2811 |
| `BoardVerse.API\BackgroundServices\WalkInWindowCleanupJob.cs` | 2216 |
| `BoardVerse.API\Controllers\AdminCafeController.cs` | 8862 |
| `BoardVerse.API\Controllers\AdminCafePartnerApplicationController.cs` | 4857 |
| `BoardVerse.API\Controllers\AdminConfigurationController.cs` | 10452 |
| `BoardVerse.API\Controllers\AdminFriendReportController.cs` | 4126 |
| `BoardVerse.API\Controllers\AdminJobsController.cs` | 13923 |
| `BoardVerse.API\Controllers\AdminMasterCatalogController.cs` | 14054 |
| `BoardVerse.API\Controllers\AdminModerationController.cs` | 21936 |
| `BoardVerse.API\Controllers\AdminReportController.cs` | 7730 |
| `BoardVerse.API\Controllers\AdminReservationController.cs` | 3554 |
| `BoardVerse.API\Controllers\AdminSettlementController.cs` | 7237 |
| `BoardVerse.API\Controllers\AdminTournamentController.cs` | 15735 |
| `BoardVerse.API\Controllers\AdminWalletController.cs` | 13338 |
| `BoardVerse.API\Controllers\AuthController.cs` | 12237 |
| `BoardVerse.API\Controllers\BaseApiController.cs` | 2386 |
| `BoardVerse.API\Controllers\BggController.cs` | 5803 |
| `BoardVerse.API\Controllers\BoardGameController.cs` | 6366 |
| `BoardVerse.API\Controllers\BookingController.cs` | 12608 |
| `BoardVerse.API\Controllers\BookingRatingController.cs` | 5611 |
| `BoardVerse.API\Program.cs` | 35659 |
| `BoardVerse.API\Program.Partial.cs` | 60 |
| `TestToken.cs` | 1142 |

_...and 920 more files (truncated for readability)_

