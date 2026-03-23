using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlaNEvent.Api.Infrastructure;

public sealed class FlexibleTimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    private static readonly string[] SupportedFormats =
    [
        @"hh\:mm\:ss",
        @"h\:mm\:ss",
        @"hh\:mm",
        @"h\:mm",
        @"hhmm",
        @"hmm"
    ];

    private static readonly string[] SupportedClockFormats =
    [
        "h:mm tt",
        "hh:mm tt",
        "h:mmtt",
        "hh:mmtt",
        "htt",
        "h tt"
    ];

    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => ParseTime(reader.GetString()),
            JsonTokenType.Number when reader.TryGetInt64(out var totalSeconds) => TimeSpan.FromSeconds(totalSeconds),
            _ => throw new JsonException("Time value must be a string like '09:00', '09:00:00', or '9:00 AM'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
    }

    internal static TimeSpan ParseTime(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new JsonException("Time value cannot be empty.");
        }

        var value = rawValue.Trim();
        if (TimeSpan.TryParseExact(value, SupportedFormats, CultureInfo.InvariantCulture, out var exact))
        {
            return exact;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParseExact(value, SupportedClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var clock))
        {
            return clock.TimeOfDay;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var genericClock))
        {
            return genericClock.TimeOfDay;
        }

        throw new JsonException($"Unsupported time value '{rawValue}'.");
    }
}

public sealed class FlexibleNullableTimeSpanJsonConverter : JsonConverter<TimeSpan?>
{
    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is JsonTokenType.String && string.IsNullOrWhiteSpace(reader.GetString()))
        {
            return null;
        }

        return FlexibleTimeSpanJsonConverter.ParseTime(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
            return;
        }

        writer.WriteNullValue();
    }
}
