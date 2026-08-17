using BoardVerse.Core.Messages;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="ApiErrorMessages.Validation.GetReservationFieldMessage"/> —
/// ánh xạ field DTO của Reservation flow sang message tiếng Việt thân thiện,
/// dùng bởi <c>InvalidModelStateResponseFactory</c> trong Program.cs.
///
/// Mục đích: khi ASP.NET Core auto-reject request (do [Required]/[Range] validation)
/// trước khi controller chạy, FE phải nhận được <c>ApiResponse</c> shape với <c>message</c>
/// tiếng Việt thân thiện — KHÔNG phải raw English ValidationProblemDetails.
/// </summary>
public class ApiErrorMessagesValidationTests
{
    #region PreferredEndTime

    [Fact]
    public void GetReservationFieldMessage_PreferredEndTime_ReturnsNotNeededMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "PreferredEndTime",
            "The PreferredEndTime field is required.");

        Assert.NotNull(msg);
        Assert.Contains("preferredEndTime", msg);
        Assert.Contains("không cần gửi", msg);
    }

    [Fact]
    public void GetReservationFieldMessage_PreferredEndTime_HandlesMissingErrorMessage()
    {
        // Vẫn trả message specific dù error message gốc null
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "PreferredEndTime",
            null);

        Assert.NotNull(msg);
        Assert.Contains("timeSlot", msg);
    }

    #endregion

    #region PreferredStartTime

    [Fact]
    public void GetReservationFieldMessage_PreferredStartTime_Required_ReturnsRequiredMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "PreferredStartTime",
            "The PreferredStartTime field is required.");

        Assert.NotNull(msg);
        Assert.Contains("preferredStartTime", msg);
        Assert.Contains("là bắt buộc", msg);
    }

    [Fact]
    public void GetReservationFieldMessage_PreferredStartTime_OutOfRange_ReturnsRangeMessage()
    {
        // Lỗi khác "is required" → trả message về timeSlot range
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "PreferredStartTime",
            "The field PreferredStartTime must be between 06:00:00 and 23:00:00.");

        Assert.NotNull(msg);
        Assert.Contains("khung giờ", msg);
        Assert.Contains("evening", msg);
    }

    #endregion

    #region TimeSlot

    [Fact]
    public void GetReservationFieldMessage_TimeSlot_ReturnsInvalidMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "TimeSlot",
            "The TimeSlot field is required.");

        Assert.NotNull(msg);
        Assert.Contains("timeSlot", msg);
        Assert.Contains("morning", msg);
        Assert.Contains("evening", msg);
    }

    #endregion

    #region PlayDate

    [Fact]
    public void GetReservationFieldMessage_PlayDate_ReturnsOutOfRangeMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "PlayDate",
            "Could not convert string to DateOnly: abc");

        Assert.NotNull(msg);
        Assert.Contains("playDate", msg);
        Assert.Contains("7 ngày", msg);
    }

    #endregion

    #region MinPlayers / MaxPlayers

    [Fact]
    public void GetReservationFieldMessage_MinPlayers_ReturnsInvalidMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "MinPlayers",
            "The field MinPlayers must be between 1 and 30.");

        Assert.NotNull(msg);
        Assert.Contains("minPlayers", msg);
        Assert.Contains("maxPlayers", msg);
    }

    [Fact]
    public void GetReservationFieldMessage_MaxPlayers_ReturnsInvalidMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "MaxPlayers",
            "The field MaxPlayers must be between 1 and 30.");

        Assert.NotNull(msg);
        Assert.Contains("Số người chơi", msg);
    }

    #endregion

    #region IdempotencyKey

    [Fact]
    public void GetReservationFieldMessage_IdempotencyKey_ReturnsInvalidMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "IdempotencyKey",
            "The field IdempotencyKey must be a string with a minimum length of 8 and a maximum length of 128.");

        Assert.NotNull(msg);
        Assert.Contains("idempotencyKey", msg);
        Assert.Contains("128 ký tự", msg);
    }

    #endregion

    #region CafeId / GameId

    [Fact]
    public void GetReservationFieldMessage_CafeId_ReturnsRequiredMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "CafeId",
            "The CafeId field is required.");

        Assert.NotNull(msg);
        Assert.Contains("cafeId", msg);
    }

    [Fact]
    public void GetReservationFieldMessage_GameId_ReturnsRequiredMessage()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "GameId",
            "The GameId field is required.");

        Assert.NotNull(msg);
        Assert.Contains("gameId", msg);
    }

    #endregion

    #region Unknown fields

    [Fact]
    public void GetReservationFieldMessage_UnknownField_ReturnsNull()
    {
        // Field không thuộc Reservation domain → factory sẽ fallback FieldValidationFailed
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage(
            "UnknownField",
            "Some error");

        Assert.Null(msg);
    }

    [Fact]
    public void GetReservationFieldMessage_EmptyFieldName_ReturnsNull()
    {
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage("", "Some error");
        Assert.Null(msg);

        var msgNull = ApiErrorMessages.Validation.GetReservationFieldMessage(null, "Some error");
        Assert.Null(msgNull);
    }

    [Fact]
    public void GetReservationFieldMessage_DollarFieldName_ReturnsNull()
    {
        // "$" là sentinel của ModelState khi body parse hoàn toàn fail
        var msg = ApiErrorMessages.Validation.GetReservationFieldMessage("$", "Some error");
        Assert.Null(msg);
    }

    #endregion

    #region Constants exposed correctly

    [Fact]
    public void ReservationPreferredEndTimeNotNeeded_IsVietnamese_ContainsGuidance()
    {
        var msg = ApiErrorMessages.Validation.ReservationPreferredEndTimeNotNeeded;

        Assert.False(string.IsNullOrWhiteSpace(msg));
        Assert.Contains("preferredEndTime", msg);
        Assert.Contains("timeSlot", msg);
        Assert.Contains("nhé", msg); // tiếng Việt thân thiện
    }

    [Fact]
    public void ReservationIdempotencyKeyInvalid_HasLengthConstraint()
    {
        var msg = ApiErrorMessages.Validation.ReservationIdempotencyKeyInvalid;

        Assert.Contains("8", msg);
        Assert.Contains("128", msg);
        Assert.Contains("idempotencyKey", msg);
    }

    #endregion
}