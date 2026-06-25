using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Json
{
    public sealed class FlexibleDateOnlyJsonConverter : JsonConverter<DateOnly?>
    {
        public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException(ApiErrorMessages.Validation.DateOfBirthFormat);
            }

            var raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (raw.Length >= 10 && DateOnly.TryParse(raw.AsSpan(0, 10), CultureInfo.InvariantCulture, out var dateOnly))
            {
                return dateOnly;
            }

            if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateOnly))
            {
                return dateOnly;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            {
                return DateOnly.FromDateTime(dateTime);
            }

            throw new JsonException(ApiErrorMessages.Validation.DateOfBirthFormat);
        }

        public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
    }
}
