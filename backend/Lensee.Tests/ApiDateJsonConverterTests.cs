using System.Text.Json;
using Lensee.Host.Infrastructure;
using Xunit;

namespace Lensee.Tests;

public sealed class ApiDateJsonConverterTests
{
    private readonly JsonSerializerOptions _options = ApiDateJsonOptions.CreateWebOptions();

    [Theory]
    [InlineData("\"29/02/2024\"", 2024, 2, 29)]
    [InlineData("\"2024-02-29\"", 2024, 2, 29)]
    public void DateOnly_ReadsCurrentAndLegacyFormats(string json, int year, int month, int day)
    {
        var parsed = JsonSerializer.Deserialize<DateOnly>(json, _options);
        Assert.Equal(new DateOnly(year, month, day), parsed);
        Assert.Equal($"\"{day:00}/{month:00}/{year:0000}\"", JsonSerializer.Serialize(parsed, _options));
    }

    [Theory]
    [InlineData("\"31/04/2025\"")]
    [InlineData("\"29/02/2025\"")]
    [InlineData("\"2025-13-01\"")]
    public void DateOnly_RejectsInvalidCalendarDates(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateOnly>(json, _options));

    [Theory]
    [InlineData("\"14/10/2026 09:08:07\"")]
    [InlineData("\"2026-10-14T09:08:07\"")]
    [InlineData("\"2026-10-14T09:08:07.0000000\"")]
    public void DateTime_ReadsTargetAndLegacyIsoFormats(string json)
    {
        var parsed = JsonSerializer.Deserialize<DateTime>(json, _options);
        Assert.Equal(new DateTime(2026, 10, 14, 9, 8, 7), parsed);
        Assert.Equal("\"14/10/2026 09:08:07\"", JsonSerializer.Serialize(parsed, _options));
    }

    [Fact]
    public void NullableDates_PreserveNull()
    {
        Assert.Null(JsonSerializer.Deserialize<DateOnly?>("null", _options));
        Assert.Null(JsonSerializer.Deserialize<DateTime?>("null", _options));
        Assert.Equal("null", JsonSerializer.Serialize<DateOnly?>(null, _options));
        Assert.Equal("null", JsonSerializer.Serialize<DateTime?>(null, _options));
    }

    [Fact]
    public void DateTimeOffset_UsesThePublicDateTimeFormat()
    {
        var value = new DateTimeOffset(2026, 10, 14, 9, 8, 7, TimeSpan.FromHours(3));

        Assert.Equal("\"14/10/2026 09:08:07\"", JsonSerializer.Serialize(value, _options));
        Assert.Equal(value.DateTime, JsonSerializer.Deserialize<DateTimeOffset>("\"14/10/2026 09:08:07\"", _options).DateTime);
        Assert.Equal(value, JsonSerializer.Deserialize<DateTimeOffset>("\"2026-10-14T09:08:07+03:00\"", _options));
    }
}
