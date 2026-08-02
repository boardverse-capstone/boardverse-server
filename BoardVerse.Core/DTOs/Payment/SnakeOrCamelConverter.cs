using System.Globalization;
using System.Text.Json;

namespace BoardVerse.Core.DTOs.Payment;

/// <summary>
/// Converter cho phép deserialize JSON với cả camelCase lẫn snake_case property names.
/// Mapping:
///
///   id                      → Id
///   gateway                 → Gateway
///   gatewayTransactionId /  gateway_transaction_id → GatewayTransactionId
///   orderId             /   order_id           → OrderId
///   amount                   → Amount
///   currency                 → Currency
///   status                   → Status
///   referenceCode /          reference_code     → ReferenceCode
///   bankCode /              bank_code          → BankCode
///   bankAccount /           bank_account       → BankAccount
///   note                     → Note
///   paidAt /                paid_at            → PaidAt
///   signature                → Signature
///   sessionId /             session_id         → SessionId
/// </summary>
public class SnakeOrCamelConverter<T> : System.Text.Json.Serialization.JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject but got {reader.TokenType}");
        }

        var dto = (T)Activator.CreateInstance(typeof(T))!;
        var props = typeof(T).GetProperties();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dto;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var rawName = reader.GetString();
            if (string.IsNullOrEmpty(rawName))
            {
                reader.Skip();
                continue;
            }

            // Map cả 2 format sang PascalCase property name
            var propName = NormalizeToPascal(rawName);
            var prop = Array.Find(props, p =>
                string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase));

            if (prop == null || !prop.CanWrite)
            {
                reader.Skip();
                continue;
            }

            reader.Read();

            try
            {
                var value = JsonSerializer.Deserialize(ref reader, prop.PropertyType, options);
                prop.SetValue(dto, value);
            }
            catch
            {
                reader.Skip();
            }
        }

        throw new JsonException("Unexpected end of JSON.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        // Serialize bình thường — không cần convert tên khi ghi ra
        JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(T), options);
    }

    private static string NormalizeToPascal(string name)
    {
        // order_id → OrderId, orderId → OrderId, orderid → Orderid
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            // camelCase hoặc PascalCase → PascalCase
            if (string.IsNullOrEmpty(parts[0]))
            {
                return name;
            }
            return char.ToUpperInvariant(parts[0][0]) + parts[0][1..];
        }

        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part[1..]);
            }
        }
        return sb.ToString();
    }
}
