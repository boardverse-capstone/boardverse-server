namespace BoardVerse.Core.Enum;

/// <summary>
/// Error codes riêng cho validation startTime/endTime (BR-RES-07/08/09 + BR-LOBBY-01a/b).
/// Throw từ <c>ReservationService.QuoteAsync / ConfirmAsync</c>.
/// </summary>
/// <remarks>
/// Mapping sang <see cref="BoardVerse.Core.Messages.ApiErrorMessages.Reservation"/>:
/// <list type="bullet">
/// <item><description><c>ReservationRequiresStartAndEnd</c> → BR-RES-07 (startTime + endTime bắt buộc).</description></item>
/// <item><description><c>ReservationEndTimeDifferentDay</c> → BR-RES-08 (endTime cùng ngày startTime).</description></item>
/// <item><description><c>ReservationInvalidTimeSlot</c> → BR-RES-09 (TimeSlot không thuộc BR-NEW-15 enum).</description></item>
/// <item><description><c>ReservationBufferTooShort</c> → BR-LOBBY-01a (cảnh báo 60-120 phút).</description></item>
/// <item><description><c>ReservationBufferTooSmall</c> → BR-LOBBY-01b (từ chối &lt; 60 phút).</description></item>
/// </list>
/// </remarks>
public enum ReservationValidationError
{
    /// <summary>BR-RES-07: Reservation phải có cả startTime và endTime.</summary>
    ReservationRequiresStartAndEnd,

    /// <summary>BR-RES-08: endTime phải cùng ngày với startTime.</summary>
    ReservationEndTimeDifferentDay,

    /// <summary>BR-RES-09: TimeSlot không thuộc BR-NEW-15 enum.</summary>
    ReservationInvalidTimeSlot,

    /// <summary>BR-LOBBY-01a: Buffer &lt; 120 phút (cảnh báo UI).</summary>
    ReservationBufferTooShort,

    /// <summary>BR-LOBBY-01b: Buffer &lt; 60 phút (từ chối).</summary>
    ReservationBufferTooSmall
}