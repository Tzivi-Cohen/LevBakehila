using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LevBakehila.API.Json;

// The API speaks UTC only.
// In:  "2026-10-21T13:00:00Z" or "2026-10-21T16:00:00+03:00" -> the same UTC moment (DateTimeKind.Utc).
//      A value without an offset is read as UTC.
// Out: always ISO 8601 with "Z", e.g. "2026-10-21T13:00:00Z". The React client shows it in Israel time.
public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.GetString();
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            throw new JsonException($"'{text}' is not a valid ISO 8601 date and time.");
        }
        return value.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }
}
