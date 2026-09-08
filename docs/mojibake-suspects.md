# Mojibake Suspects

- Generated: 2026-09-07 21:53:17
- Heuristic: high '?' density (>0.5%) + many Latin-1 supplement chars (Windows-1252 Vietnamese range)
- These files likely contain `?` literal where Vietnamese diacritics should be.

- Suspect count: 18

| Path | Size (bytes) | '?' count | Density (%) | Latin-1 VN chars |
|------|-------------:|----------:|------------:|------------------:|
| `BoardVerse.Core\DTOs\Reservation\ReservationDtos.cs` | 32816 | 430 | 1.31 | 389 |
| `BoardVerse.Core\DTOs\Payment\SePayAccountDto.cs` | 6146 | 47 | 0.76 | 141 |
| `BoardVerse.Core\Entities\TournamentMatchBracket.cs` | 3655 | 25 | 0.68 | 106 |
| `BoardVerse.Core\DTOs\Tournament\TournamentMatchDtos.cs` | 3158 | 21 | 0.66 | 73 |
| `BoardVerse.Core\DTOs\Admin\AdminBoardGameDtos.cs` | 2271 | 12 | 0.53 | 63 |
| `BoardVerse.Core\Entities\UserProfile.cs` | 3618 | 20 | 0.55 | 61 |
| `BoardVerse.Core\Entities\BookingDeposit.cs` | 2366 | 16 | 0.68 | 61 |
| `BoardVerse.Services\Services\UserProfileService.cs` | 24172 | 121 | 0.5 | 55 |
| `BoardVerse.API\Controllers\ProtectedController.cs` | 1306 | 8 | 0.61 | 46 |
| `BoardVerse.Core\DTOs\Tournament\UpdateTournamentRequestDto.cs` | 1636 | 14 | 0.86 | 35 |
| `BoardVerse.API\Controllers\BaseApiController.cs` | 2386 | 13 | 0.54 | 35 |
| `BoardVerse.Core\DTOs\Booking\BookingResponseDto.cs` | 3690 | 30 | 0.81 | 32 |
| `BoardVerse.Core\DTOs\Admin\SettlementListItemDto.cs` | 1418 | 11 | 0.78 | 28 |
| `BoardVerse.Core\DTOs\Booking\UpdateBookingRequestDto.cs` | 693 | 4 | 0.58 | 27 |
| `BoardVerse.Services\Helpers\DemoGuard.cs` | 4058 | 22 | 0.54 | 26 |
| `BoardVerse.Services\Services\Geocoding\NominatimResponseParser.cs` | 5629 | 50 | 0.89 | 19 |
| `BoardVerse.Core\Entities\CafeSettlement.cs` | 1549 | 10 | 0.65 | 17 |
| `BoardVerse.Core\DTOs\Payment\CreatePaymentResponseDto.cs` | 596 | 3 | 0.5 | 15 |
