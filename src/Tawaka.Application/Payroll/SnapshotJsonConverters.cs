using System.Text.Json;
using System.Text.Json.Serialization;
using Tawaka.Domain.Common;

namespace Tawaka.Application.Payroll;

/// <summary>
/// Writes a currency as its three-letter code and reads it back through the constructor.
/// <para>
/// Without this, <see cref="CurrencyCode"/> round-trips to an <em>empty</em> struct: it holds a
/// private field with no matching settable property, so the serialiser writes the derived
/// <c>Value</c> and has nowhere to put it on the way back. A stored snapshot would then come back
/// with no currency at all — the exact failure this system is built to prevent.
/// </para>
/// </summary>
public sealed class CurrencyCodeJsonConverter : JsonConverter<CurrencyCode>
{
    public override CurrencyCode Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new CurrencyCode(reader.GetString()!);
        }

        // Tolerates the object form {"Value":"USD"} that an earlier snapshot may have been written
        // in, so historical rows stay readable.
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value) &&
                value.GetString() is { } code)
            {
                return new CurrencyCode(code);
            }
        }

        throw new JsonException("A currency code must be a three-letter string.");
    }

    public override void Write(
        Utf8JsonWriter writer, CurrencyCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.IsEmpty ? null : value.Value);
}

/// <summary>
/// Writes money as its amount and currency together, and refuses to read one without the other.
/// <para>
/// The pairing is the whole point of the type: an amount that loses its currency on the way into
/// storage is how a ZiG figure comes back as a USD one.
/// </para>
/// </summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty(nameof(Money.Amount), out var amount) ||
            !root.TryGetProperty(nameof(Money.Currency), out var currency))
        {
            throw new JsonException("Money must carry both an amount and a currency.");
        }

        var code = currency.ValueKind == JsonValueKind.String
            ? currency.GetString()
            : currency.TryGetProperty("Value", out var nested) ? nested.GetString() : null;

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new JsonException("Money must carry a currency.");
        }

        return new Money(amount.GetDecimal(), new CurrencyCode(code));
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(Money.Amount), value.Amount);
        writer.WriteString(nameof(Money.Currency), value.Currency.Value);
        writer.WriteEndObject();
    }
}
