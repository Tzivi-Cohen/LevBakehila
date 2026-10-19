using System.Text.Json;
using LevBakehila.API.Json;

namespace LevBakehila.Tests.Unit;

public class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeJsonConverter() } };

    [Theory]
    [InlineData("\"2026-10-21T13:00:00Z\"")]
    [InlineData("\"2026-10-21T16:00:00+03:00\"")]   // Israel summer time
    [InlineData("\"2026-10-21T13:00:00\"")]          // no offset = UTC
    public void Read_AnyIsoFormat_ReturnsTheSameUtcMoment(string json)
    {
        var value = JsonSerializer.Deserialize<DateTime>(json, Options);

        Assert.Equal(new DateTime(2026, 10, 21, 13, 0, 0, DateTimeKind.Utc), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Fact]
    public void Write_AlwaysUtcWithZ()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc), Options);

        Assert.Equal("\"2026-10-25T15:00:00Z\"", json);
    }

    [Fact]
    public void Read_InvalidDate_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"tomorrow\"", Options));
    }
}
