using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Helpers
{
    /// <summary>
    /// Pure helper áp dụng partial update cho một bàn (Name/SeatCount/SortOrder).
    /// Tách khỏi POS service để dễ unit-test; service chỉ lo access check + persist.
    /// </summary>
    public static class CafeTableUpdateHelper
    {
        public const int MaxSeatsPerTable = 50;

        /// <summary>
        /// Validate và áp dụng các field từ request lên table.
        /// Throw BadRequestException nếu all-null; ConflictException nếu tên trùng.
        /// </summary>
        /// <param name="table">Bàn hiện tại (sẽ mutate).</param>
        /// <param name="request">DTO PATCH từ client.</param>
        /// <param name="existingActiveTables">Snapshot các bàn đang active trong quán (để check unique name).</param>
        public static void ApplyUpdate(
            CafeTable table,
            UpdateCafeTableRequestDto request,
            IEnumerable<CafeTable> existingActiveTables)
        {
            if (request.Name == null && request.SeatCount == null && request.SortOrder == null)
            {
                throw new BadRequestException(ApiErrorMessages.Validation.TableNoFieldsToUpdate);
            }

            if (request.Name != null)
            {
                var trimmedName = request.Name.Trim();
                if (string.IsNullOrWhiteSpace(trimmedName))
                {
                    throw new BadRequestException(ApiErrorMessages.Validation.TableNameLength);
                }

                var nameClash = existingActiveTables.FirstOrDefault(t =>
                    t.Id != table.Id
                    && string.Equals(t.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
                if (nameClash != null)
                {
                    throw new ConflictException(ApiErrorMessages.Pos.TableNameAlreadyExists(table.CafeId, trimmedName));
                }

                table.Name = trimmedName;
            }

            if (request.SeatCount.HasValue)
            {
                if (request.SeatCount.Value < 1 || request.SeatCount.Value > MaxSeatsPerTable)
                {
                    throw new BadRequestException(ApiErrorMessages.Validation.SeatsPerTableRange);
                }
                table.SeatCount = request.SeatCount.Value;
            }

            if (request.SortOrder.HasValue)
            {
                table.SortOrder = request.SortOrder.Value;
            }

            table.UpdatedAt = DateTime.UtcNow;
        }
    }
}
