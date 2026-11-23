using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrologMobile.Inventory.Api;

public sealed class UtcTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var utc = value.ToUniversalTime();
        var hasFraction = utc.Ticks % TimeSpan.TicksPerSecond != 0;
        var format = hasFraction
            ? "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"
            : "yyyy-MM-dd'T'HH:mm:ss'Z'";
        writer.WriteStringValue(utc.ToString(format, CultureInfo.InvariantCulture));
    }
}

public sealed class MoneyJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}
