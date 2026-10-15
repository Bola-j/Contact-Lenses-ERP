using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lensee.Host.Infrastructure;

/// <summary>Wire-format converters for dates returned by the public HTTP API.</summary>
public sealed class ApiDateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private static readonly string[] AcceptedFormats = ["dd/MM/yyyy", "yyyy-MM-dd"];

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !DateOnly.TryParseExact(reader.GetString(), AcceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new JsonException("Date must use dd/MM/yyyy or the legacy yyyy-MM-dd format.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));

    public override DateOnly ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateOnly.TryParseExact(reader.GetString(), AcceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : throw new JsonException("Date dictionary key must use dd/MM/yyyy or the legacy yyyy-MM-dd format.");

    public override void WriteAsPropertyName(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
}

public sealed class ApiNullableDateOnlyJsonConverter : JsonConverter<DateOnly?>
{
    private readonly ApiDateOnlyJsonConverter _inner = new();
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : _inner.Read(ref reader, typeof(DateOnly), options);
    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value.HasValue) _inner.Write(writer, value.Value, options);
        else writer.WriteNullValue();
    }
}

public sealed class ApiDateTimeJsonConverter : JsonConverter<DateTime>
{
    private static readonly string[] AcceptedFormats =
    [
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy",
        "O", "o", "s", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss"
    ];

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !DateTime.TryParseExact(reader.GetString(), AcceptedFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var value))
            throw new JsonException("Date-time must use dd/MM/yyyy HH:mm:ss or a legacy ISO 8601 format.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
}

public sealed class ApiNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    private readonly ApiDateTimeJsonConverter _inner = new();
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : _inner.Read(ref reader, typeof(DateTime), options);
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue) _inner.Write(writer, value.Value, options);
        else writer.WriteNullValue();
    }
}

public sealed class ApiDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private static readonly string[] AcceptedFormats =
    [
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy",
        "O", "o", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss"
    ];

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !DateTimeOffset.TryParseExact(reader.GetString(), AcceptedFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var value))
            throw new JsonException("Date-time must use dd/MM/yyyy HH:mm:ss or a legacy ISO 8601 format.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
}

public sealed class ApiNullableDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset?>
{
    private readonly ApiDateTimeOffsetJsonConverter _inner = new();
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : _inner.Read(ref reader, typeof(DateTimeOffset), options);
    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue) _inner.Write(writer, value.Value, options);
        else writer.WriteNullValue();
    }
}

public static class ApiDateJsonOptions
{
    public static void AddConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new ApiDateOnlyJsonConverter());
        options.Converters.Add(new ApiNullableDateOnlyJsonConverter());
        options.Converters.Add(new ApiDateTimeJsonConverter());
        options.Converters.Add(new ApiNullableDateTimeJsonConverter());
        options.Converters.Add(new ApiDateTimeOffsetJsonConverter());
        options.Converters.Add(new ApiNullableDateTimeOffsetJsonConverter());
    }

    public static JsonSerializerOptions CreateWebOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        AddConverters(options);
        return options;
    }
}
