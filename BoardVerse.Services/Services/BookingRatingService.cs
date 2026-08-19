using System.Text.Json;
using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Triển khai voting/rating cho booking (mobile gap #4 + #5).
/// </summary>
public class BookingRatingService : IBookingRatingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly IBookingNoShowVoteRepository _noShowVoteRepository;
    private readonly IBookingRatingRepository _ratingRepository;
    private readonly IBookingDepositRepository _depositRepository;
    private readonly IKarmaRatingRepository _karmaRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly ILogger<BookingRatingService> _logger;

    public BookingRatingService(
        IBookingRepository bookingRepository,
        ILobbyRepository lobbyRepository,
        IBookingNoShowVoteRepository noShowVoteRepository,
        IBookingRatingRepository ratingRepository,
        IBookingDepositRepository depositRepository,
        IKarmaRatingRepository karmaRepository,
        IUserProfileRepository userProfileRepository,
        ILogger<BookingRatingService> logger)
    {
        _bookingRepository = bookingRepository;
        _lobbyRepository = lobbyRepository;
        _noShowVoteRepository = noShowVoteRepository;
        _ratingRepository = ratingRepository;
        _depositRepository = depositRepository;
        _karmaRepository = karmaRepository;
        _userProfileRepository = userProfileRepository;
        _logger = logger;
    }

    public async Task<NoShowVoteResponseDto> SubmitNoShowVoteAsync(
        Guid bookingId, Guid voterUserId, SubmitNoShowVoteRequestDto request)
    {
        if (request.BookingId != bookingId)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.BookingIdMismatch);
        }

        if (request.AbsentMemberIds.Contains(voterUserId))
        {
            throw new BadRequestException(ApiErrorMessages.Booking.CannotVoteSelfAbsent);
        }

        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.BookingNotFoundById(bookingId));

        if (booking.Status != BookingStatus.CheckedIn)
        {
            throw new ConflictException(
                booking.Status == BookingStatus.Cancelled
                    ? ApiErrorMessages.Booking.AlreadyCheckedOut
                    : ApiErrorMessages.Booking.NotCheckedInYet);
        }

        // Vote window: từ CheckInAt + 30 phút (tránh vote ngay khi vừa check-in)
        // đến ScheduleEndTime + 24h (mobile gap #4 — chuẩn md booking-payment-gaps.md).
        var nowUtc = DateTime.UtcNow;
        var voteDeadline = booking.ScheduleEndTime.AddHours(24);

        if (booking.CheckedInAt.HasValue)
        {
            var voteOpensAt = booking.CheckedInAt.Value.AddMinutes(30);
            if (nowUtc < voteOpensAt)
            {
                throw new ConflictException(ApiErrorMessages.Booking.VoteOpensAtTime(voteOpensAt));
            }
        }
        // Booking chưa có CheckedInAt (walk-in edge case hoặc data migration cũ) → bỏ qua check 30 phút.

        if (nowUtc > voteDeadline)
        {
            throw new ConflictException(ApiErrorMessages.Booking.VoteWindowClosed);
        }

        // Voter phải là member lobby active.
        if (booking.Lobby == null)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.WalkInBookingNoShowVoteNotSupported);
        }
        var lobbyMembers = await _lobbyRepository.GetByIdWithMembersAsync(booking.LobbyId!.Value)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.LobbyNotFoundById);
        var lobbyMemberIds = lobbyMembers.Members
            .Where(m => m.IsActive)
            .Select(m => m.UserId)
            .ToHashSet();
        if (!lobbyMemberIds.Contains(voterUserId))
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.VoterNotCheckedInMember);
        }

        // Validate tất cả absentMemberIds đều là member lobby.
        var invalidIds = request.AbsentMemberIds.Where(id => !lobbyMemberIds.Contains(id)).ToList();
        if (invalidIds.Count > 0)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.NotLobbyMemberIdsJoin(invalidIds));
        }

        // Idempotent: insert hoặc update vote của voter này.
        var existingVote = await _noShowVoteRepository.GetByBookingAndVoterAsync(bookingId, voterUserId);
        if (existingVote == null)
        {
            var vote = new BookingNoShowVote
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                VoterUserId = voterUserId,
                AbsentMemberIdsJson = JsonSerializer.Serialize(request.AbsentMemberIds),
                VotedAt = request.VotedAt
            };
            await _noShowVoteRepository.AddAsync(vote);
        }
        else
        {
            existingVote.AbsentMemberIdsJson = JsonSerializer.Serialize(request.AbsentMemberIds);
            existingVote.UpdatedAt = DateTime.UtcNow;
            existingVote.VotedAt = request.VotedAt;
            await _noShowVoteRepository.UpdateAsync(existingVote);
        }
        await _noShowVoteRepository.SaveChangesAsync();

        // Aggregate votes → trả voteCounts + noShowConfirmedMembers.
        var allVotes = await _noShowVoteRepository.GetByBookingAsync(bookingId);
        var totalMembers = lobbyMemberIds.Count;

        // Đếm absentVotes cho mỗi userId.
        var absentVoteCounts = new Dictionary<Guid, int>();
        foreach (var v in allVotes)
        {
            var ids = JsonSerializer.Deserialize<List<Guid>>(v.AbsentMemberIdsJson) ?? new();
            foreach (var id in ids)
            {
                absentVoteCounts[id] = absentVoteCounts.GetValueOrDefault(id, 0) + 1;
            }
        }

        var voteCounts = new Dictionary<Guid, NoShowVoteCountDto>();
        foreach (var memberId in lobbyMemberIds)
        {
            var absent = absentVoteCounts.GetValueOrDefault(memberId, 0);
            var present = allVotes.Count - absent;
            voteCounts[memberId] = new NoShowVoteCountDto
            {
                AbsentVotes = absent,
                PresentVotes = Math.Max(0, present),
                TotalMembers = totalMembers
            };
        }

        // NoShow confirmed: absentVotes > totalMembers/2.
        var threshold = totalMembers / 2;
        var confirmed = voteCounts
            .Where(kv => kv.Value.AbsentVotes > threshold)
            .Select(kv => kv.Key)
            .ToList();

        return new NoShowVoteResponseDto
        {
            BookingId = bookingId,
            VoterId = voterUserId,
            AbsentMemberIds = request.AbsentMemberIds,
            CurrentVoteCounts = voteCounts,
            NoShowConfirmedMembers = confirmed,
            ProcessedAt = null
        };
    }

    public async Task<BookingRatingResponseDto> SubmitRatingsAsync(
        Guid bookingId, Guid voterUserId, SubmitBookingRatingsRequestDto request)
    {
        if (request.BookingId != bookingId)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.BookingIdMismatch);
        }

        // Validate từng rating.
        var ratedIds = request.Ratings.Select(r => r.RatedUserId).ToList();
        if (ratedIds.Contains(voterUserId))
        {
            throw new BadRequestException(ApiErrorMessages.Booking.CannotRateSelf);
        }
        if (ratedIds.Count != ratedIds.Distinct().Count())
        {
            throw new BadRequestException(ApiErrorMessages.Booking.DuplicateRatedUser);
        }
        foreach (var item in request.Ratings)
        {
            if (item.Attitude is < 1 or > 5
                || item.Sportsmanship is < 1 or > 5
                || item.Punctuality is < 1 or > 5)
            {
                throw new BadRequestException(ApiErrorMessages.Booking.RatingScoreOutOfRange);
            }
            if (!string.IsNullOrEmpty(item.Comment) && item.Comment.Length > 500)
            {
                throw new BadRequestException(ApiErrorMessages.Booking.RatingCommentTooLong);
            }
        }

        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.BookingNotFoundById(bookingId));

        // BR: rate được khi booking đã check-in hoặc vừa check-out (còn trong 24h).
        if (booking.Status == BookingStatus.PendingDeposit
            || booking.Status == BookingStatus.Cancelled
            || booking.Status == BookingStatus.NoShow)
        {
            throw new ConflictException(ApiErrorMessages.Booking.BookingNotYetEligibleForRating);
        }

        // Voter phải là member lobby active.
        if (booking.LobbyId == null)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.WalkInBookingRatingNotSupported);
        }
        var lobby = await _lobbyRepository.GetByIdWithMembersAsync(booking.LobbyId.Value)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.LobbyNotFoundById);
        var lobbyMemberIds = lobby.Members.Where(m => m.IsActive).Select(m => m.UserId).ToHashSet();
        if (!lobbyMemberIds.Contains(voterUserId))
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.OnlyLobbyMemberCanRate);
        }

        // Tất cả ratedUserIds phải là member lobby (trừ voter).
        var invalid = ratedIds.Where(id => !lobbyMemberIds.Contains(id)).ToList();
        if (invalid.Count > 0)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.NotLobbyMemberIdsJoin(invalid));
        }

        // Idempotent: insert hoặc update.
        var existing = await _ratingRepository.GetByBookingAndVoterAsync(bookingId, voterUserId);
        var nowUtc = DateTime.UtcNow;

        if (existing == null)
        {
            var rating = new BookingRating
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                VoterUserId = voterUserId,
                RatingsJson = JsonSerializer.Serialize(request.Ratings),
                SubmittedAt = nowUtc,
                IsAggregated = false
            };
            await _ratingRepository.AddAsync(rating);
        }
        else
        {
            existing.RatingsJson = JsonSerializer.Serialize(request.Ratings);
            // SubmittedAt giữ nguyên (audit first-submit time).
            await _ratingRepository.UpdateAsync(existing);
        }
        await _ratingRepository.SaveChangesAsync();

        return new BookingRatingResponseDto
        {
            BookingId = bookingId,
            VoterId = voterUserId,
            SubmittedAt = nowUtc,
            RatedCount = request.Ratings.Count
        };
    }

    public async Task<BookingRatingStatusDto> GetRatingStatusAsync(
        Guid bookingId, Guid voterUserId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.BookingNotFoundById(bookingId));

        // Chỉ member lobby active mới xem được.
        if (booking.LobbyId == null)
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.WalkInBookingHasNoRating);
        }
        var lobby = await _lobbyRepository.GetByIdWithMembersAsync(booking.LobbyId.Value)
            ?? throw new NotFoundException(ApiErrorMessages.Lobby.LobbyNotFoundById);
        var lobbyMembers = lobby.Members.Where(m => m.IsActive).ToList();
        var lobbyMemberIds = lobbyMembers.Select(m => m.UserId).ToHashSet();
        if (!lobbyMemberIds.Contains(voterUserId))
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.OnlyLobbyMemberCanViewRating);
        }

        var canRate = booking.Status is BookingStatus.CheckedIn or BookingStatus.Confirmed;
        var existing = await _ratingRepository.GetByBookingAndVoterAsync(bookingId, voterUserId);
        var ratedIds = new List<Guid>();
        if (existing != null)
        {
            var items = JsonSerializer.Deserialize<List<BookingRatingItemDto>>(existing.RatingsJson) ?? new();
            ratedIds = items.Select(i => i.RatedUserId).ToList();
        }

        // Members chưa được rate (trừ voter).
        var missing = lobbyMemberIds
            .Where(id => id != voterUserId && !ratedIds.Contains(id))
            .ToList();

        return new BookingRatingStatusDto
        {
            BookingId = bookingId,
            CanRate = canRate,
            RateDeadline = booking.ScheduleEndTime.AddHours(24),
            AlreadyRated = existing != null,
            RatedUserIds = ratedIds,
            MissingMemberIds = missing
        };
    }

    // ===========================================================================
    // 5. AggregateBookingOutcomesAsync — staff bấm check-out booking
    // ===========================================================================

    /// <summary>Điểm Karma trừ cho mỗi user bị no-show confirmed (BR Exception 2).</summary>
    private const int NoShowKarmaPenalty = 10;

    /// <summary>Multiplier cho cross-rating: delta = (avg - 3.0) * KarmaMultiplier.</summary>
    private const int CrossRatingMultiplier = 10;

    public async Task<BookingRatingAggregationResultDto> AggregateBookingOutcomesAsync(
        Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.BookingNotFoundById(bookingId));

        // BR: chỉ aggregate sau khi đã check-out (Booking.CheckedIn + session PAID).
        // Ở đây cho phép CheckedIn/Confirmed để staff có thể aggregate ngay khi session
        // kết thúc (giữa các vòng check-out) — checkpoint thực sự ở CheckOutAsync.
        if (booking.Status == BookingStatus.PendingDeposit
            || booking.Status == BookingStatus.Cancelled)
        {
            throw new ConflictException(ApiErrorMessages.Booking.CannotAggregateBookingStatus(booking.Status));
        }

        // Idempotency check: nếu tất cả rating rows đã aggregate thì skip (chỉ re-run no-show nếu có vote mới).
        var ratingRows = await _ratingRepository.GetUnaggregatedByBookingAsync(bookingId);
        var voteRows = await _noShowVoteRepository.GetByBookingAsync(bookingId);

        // Tập member lobby active (dùng cho cả cross-rating + no-show).
        var lobbyMemberIds = new HashSet<Guid>();
        if (booking.LobbyId.HasValue)
        {
            var lobby = await _lobbyRepository.GetByIdWithMembersAsync(booking.LobbyId.Value);
            if (lobby != null)
            {
                lobbyMemberIds = lobby.Members
                    .Where(m => m.IsActive)
                    .Select(m => m.UserId)
                    .ToHashSet();
            }
        }

        var result = new BookingRatingAggregationResultDto
        {
            BookingId = bookingId,
            AggregatedAt = DateTime.UtcNow,
            RatingsProcessed = ratingRows.Count
        };

        // ---- 1) Cross-rating aggregate ----
        // Map: targetUserId → list of avg-of-3-scores per rater.
        var scoresByTarget = new Dictionary<Guid, List<decimal>>();
        foreach (var row in ratingRows)
        {
            var items = JsonSerializer.Deserialize<List<BookingRatingItemDto>>(row.RatingsJson)
                ?? new List<BookingRatingItemDto>();
            foreach (var it in items)
            {
                if (it.RatedUserId == row.VoterUserId) continue; // auto-skip self (defensive)
                var avg = (it.Attitude + it.Sportsmanship + it.Punctuality) / 3m;
                if (!scoresByTarget.TryGetValue(it.RatedUserId, out var list))
                {
                    list = new List<decimal>();
                    scoresByTarget[it.RatedUserId] = list;
                }
                list.Add(avg);
            }
        }

        foreach (var (targetId, scores) in scoresByTarget)
        {
            if (scores.Count == 0) continue;
            var mean = scores.Average();
            var delta = Math.Round((mean - 3m) * CrossRatingMultiplier, 2);
            await ApplyKarmaDeltaAsync(
                targetId,
                bookingId,
                KarmaLogSource.PlayerCrossRating,
                KarmaViolationCategory.CrossRating,
                delta,
                $"Cross-rating avg={mean:F2} over {scores.Count} voters; delta={delta:+0.00;-0.00;0}.");
            result.KarmaDeltaByUser[targetId] =
                result.KarmaDeltaByUser.GetValueOrDefault(targetId, 0m) + delta;
        }

        // Đánh dấu các rating rows đã aggregate (idempotent cho lần sau).
        foreach (var row in ratingRows)
        {
            row.IsAggregated = true;
            await _ratingRepository.UpdateAsync(row);
        }

        // ---- 2) No-show aggregate ----
        if (voteRows.Count > 0 && lobbyMemberIds.Count > 0)
        {
            var absentCount = new Dictionary<Guid, int>();
            foreach (var v in voteRows)
            {
                var ids = JsonSerializer.Deserialize<List<Guid>>(v.AbsentMemberIdsJson)
                    ?? new List<Guid>();
                foreach (var id in ids)
                {
                    if (!lobbyMemberIds.Contains(id)) continue;
                    absentCount[id] = absentCount.GetValueOrDefault(id, 0) + 1;
                }
            }

            var threshold = lobbyMemberIds.Count / 2;
            var confirmed = absentCount
                .Where(kv => kv.Value > threshold)
                .Select(kv => kv.Key)
                .ToList();

            // M5: Batch fetch profiles cho tất cả targets (cross-rating + no-show).
            // Trước đây mỗi ApplyKarmaDeltaAsync gọi GetProfileForUpdateAsync riêng (N+1).
            // Pre-fetch bằng AsNoTracking để log user nào không tồn tại.
            var allTargetIds = scoresByTarget.Keys.Concat(confirmed).Distinct().ToList();
            if (allTargetIds.Count > 0)
            {
                var existingProfiles = await _userProfileRepository.GetProfilesByUserIdsAsync(allTargetIds);
                var missingIds = allTargetIds.Where(id => !existingProfiles.ContainsKey(id)).ToList();
                if (missingIds.Count > 0)
                {
                    _logger.LogWarning(
                        "Karma delta — {Count} target users have no UserProfile (BookingId={BookingId}): {MissingIds}",
                        missingIds.Count, bookingId, string.Join(",", missingIds));
                }
            }

            // ---- 3) Forfeit deposit cho từng no-show member (nếu RefundPolicy = None) ----
            var deposit = await _depositRepository.GetByBookingIdAsync(bookingId);
            var forfeitedDepositIds = new List<Guid>();
            foreach (var memberId in confirmed)
            {
                await ApplyKarmaDeltaAsync(
                    memberId,
                    bookingId,
                    KarmaLogSource.SystemAutomatic,
                    KarmaViolationCategory.NoShow,
                    -NoShowKarmaPenalty,
                    $"No-show confirmed (absent votes > {threshold}/{lobbyMemberIds.Count}); penalty=-{NoShowKarmaPenalty}.");
                result.KarmaDeltaByUser[memberId] =
                    result.KarmaDeltaByUser.GetValueOrDefault(memberId, 0m) - NoShowKarmaPenalty;

                // Forfeit deposit: BR-18 — chỉ khi RefundPolicy = None.
                if (deposit != null
                    && deposit.UserId == memberId
                    && deposit.Status == BookingDepositStatus.Paid
                    && deposit.RefundPolicy == DepositRefundPolicy.None)
                {
                    deposit.Status = BookingDepositStatus.Forfeited;
                    await _depositRepository.UpdateAsync(deposit);
                    forfeitedDepositIds.Add(deposit.Id);

                    await ApplyKarmaLogAsync(
                        karmaDelta: 0,
                        userId: memberId,
                        bookingId: bookingId,
                        source: KarmaLogSource.SystemAutomatic,
                        category: KarmaViolationCategory.NoShow,
                        reason: $"No-show confirmed — deposit ID={deposit.Id} tịch thu (policy={deposit.RefundPolicy}).",
                        karmaBefore: 0,
                        karmaAfter: 0);
                }
            }

            result.NoShowConfirmedMembers = confirmed;
            result.ForfeitedDepositIds = forfeitedDepositIds;
        }

        // Save tất cả thay đổi vào DB (KarmaLog rows + profile updates + rating IsAggregated + deposit).
        await _karmaRepository.SaveChangesAsync();
        await _ratingRepository.SaveChangesAsync();
        await _depositRepository.SaveChangesAsync();

        result.TotalKarmaDelta = result.KarmaDeltaByUser.Values.Sum();
        return result;
    }

    /// <summary>
    /// Cộng/trừ KarmaPoints cho UserProfile + ghi <see cref="KarmaLog"/> audit row.
    /// Lưu ý: <see cref="KarmaLog.KarmaPointsChange"/> / <c>KarmaBefore</c> / <c>KarmaAfter</c>
    /// đều là <c>int</c> (theo entity), nên làm tròn delta.
    /// </summary>
    private async Task ApplyKarmaDeltaAsync(
        Guid userId,
        Guid bookingId,
        KarmaLogSource source,
        KarmaViolationCategory category,
        decimal delta,
        string reason)
    {
        var profile = await _karmaRepository.GetProfileForUpdateAsync(userId);
        if (profile == null)
        {
            _logger.LogWarning(
                "Skipping Karma delta — UserProfile not found. UserId={UserId}, BookingId={BookingId}",
                userId, bookingId);
            return;
        }

        var before = profile.KarmaPoints;
        var intDelta = (int)Math.Round(delta);
        var after = before + intDelta;

        profile.KarmaPoints = after;
        profile.UpdatedAt = DateTime.UtcNow;

        await _karmaRepository.AddKarmaLogAsync(new KarmaLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ViolationCategory = category,
            Source = source,
            KarmaPointsChange = intDelta,
            KarmaBefore = before,
            KarmaAfter = after,
            Reason = reason,
            RelatedLobbyId = bookingId, // dùng RelatedLobbyId làm correlation id (booking hoặc lobby).
            PerformedByUserId = null,
            IsAdminAdjustment = false,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Ghi <see cref="KarmaLog"/> row mà không áp dụng delta (vd: audit-only cho deposit forfeit).
    /// </summary>
    private async Task ApplyKarmaLogAsync(
        int karmaDelta,
        Guid userId,
        Guid bookingId,
        KarmaLogSource source,
        KarmaViolationCategory category,
        string reason,
        int karmaBefore,
        int karmaAfter)
    {
        await _karmaRepository.AddKarmaLogAsync(new KarmaLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ViolationCategory = category,
            Source = source,
            KarmaPointsChange = karmaDelta,
            KarmaBefore = karmaBefore,
            KarmaAfter = karmaAfter,
            Reason = reason,
            RelatedLobbyId = bookingId,
            PerformedByUserId = null,
            IsAdminAdjustment = false,
            CreatedAt = DateTime.UtcNow
        });
    }
}