using Google.Cloud.Firestore;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Services.Contracts;

/// <summary>
/// Custom JSON converter for Firestore Timestamp.
/// Handles both {"_seconds": N, "_nanoseconds": N} (Firestore native)
/// and ISO 8601 string formats.
/// </summary>
public class TimestampJsonConverter : JsonConverter<Timestamp>
{
    public override Timestamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            long seconds = 0;
            int nanos = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propName = reader.GetString();
                    reader.Read();

                    switch (propName)
                    {
                        case "_seconds":
                        case "seconds":
                            seconds = reader.TryGetInt64(out var s) ? s : long.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
                            break;
                        case "_nanoseconds":
                        case "nanos":
                        case "nanoseconds":
                            nanos = reader.TryGetInt32(out var n) ? n : int.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
                            break;
                    }
                }
            }

            var dto = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanos / 100);
            return Timestamp.FromDateTimeOffset(dto);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return default;
            }

            if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                return Timestamp.FromDateTimeOffset(dto);
            }
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            var seconds = reader.GetInt64();
            return Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds(seconds));
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, Timestamp value, JsonSerializerOptions options)
    {
        var dto = value.ToDateTimeOffset();
        writer.WriteStringValue(dto.ToString("O"));
    }
}

/// <summary>
/// Nullable variant for Timestamp? properties.
/// </summary>
public class NullableTimestampJsonConverter : JsonConverter<Timestamp?>
{
    public override Timestamp? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var converter = new TimestampJsonConverter();
        return converter.Read(ref reader, typeof(Timestamp), options);
    }

    public override void Write(Utf8JsonWriter writer, Timestamp? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            var converter = new TimestampJsonConverter();
            converter.Write(writer, value.Value, options);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
