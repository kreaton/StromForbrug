using System.Text.Json.Serialization;

namespace Eloverblik;

/// <summary>Resolution asked of /meterdata/gettimeseries. The API names them exactly like this.</summary>
public enum Aggregation
{
    Actual,
    Quarter,
    Hour,
    Day,
    Month,
    Year
}

/// <summary>One measured interval: kWh used from <see cref="StartUtc"/> for <see cref="Duration"/>.</summary>
/// <param name="StartUtc">Start of the interval, UTC.</param>
/// <param name="Duration">Length of the interval (PT1H for hourly data).</param>
/// <param name="Kwh">Quantity in kWh.</param>
/// <param name="Quality">
/// The DataHub quality code: A04 measured, A03 estimated, A05 incomplete, A01 adjusted.
/// Null when the API left it out.
/// </param>
public readonly record struct MeterReading(DateTimeOffset StartUtc, TimeSpan Duration, double Kwh, string? Quality)
{
    public DateTimeOffset EndUtc => StartUtc + Duration;

    /// <summary>True unless the value was actually measured (quality A04, or no quality given).</summary>
    public bool IsEstimated => Quality is not null and not "A04";
}

/// <summary>
/// A short-lived data access token, exchanged from the long-lived refresh token. Persist it
/// (it is valid for about 24 hours) to avoid calling the rate-limited token endpoint on every start.
/// </summary>
/// <param name="Value">The bearer token.</param>
/// <param name="ExpiresAt">When to stop using it; set a little before the real expiry.</param>
/// <param name="RefreshTokenKey">Fingerprint of the refresh token it was made from, see <see cref="EloverblikClient.Fingerprint"/>.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, string RefreshTokenKey);

/// <summary>A metering point (målepunkt) the refresh token gives access to.</summary>
public sealed class MeteringPoint
{
    [JsonPropertyName("meteringPointId")] public string MeteringPointId { get; set; } = "";

    /// <summary>E17 consumption, E18 production, D-codes for child points.</summary>
    [JsonPropertyName("typeOfMP")] public string? TypeOfMp { get; set; }

    [JsonPropertyName("streetName")] public string? StreetName { get; set; }
    [JsonPropertyName("buildingNumber")] public string? BuildingNumber { get; set; }
    [JsonPropertyName("floorId")] public string? FloorId { get; set; }
    [JsonPropertyName("roomId")] public string? RoomId { get; set; }
    [JsonPropertyName("postcode")] public string? Postcode { get; set; }
    [JsonPropertyName("cityName")] public string? CityName { get; set; }
    [JsonPropertyName("balanceSupplierName")] public string? BalanceSupplierName { get; set; }
    [JsonPropertyName("meterReadingOccurrence")] public string? MeterReadingOccurrence { get; set; }

    public bool IsConsumption => string.Equals(TypeOfMp, "E17", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Vejnavn 12, 1. tv, 8000 Aarhus C", skipping the parts that are missing.</summary>
    public string Address
    {
        get
        {
            var street = string.Join(" ", new[] { StreetName, BuildingNumber }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var floor = string.Join(" ", new[] { FloorId is { Length: > 0 } f ? f + "." : null, RoomId }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var city = string.Join(" ", new[] { Postcode, CityName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.Join(", ", new[] { street, floor, city }.Where(s => s.Length > 0));
        }
    }

    public override string ToString()
    {
        var address = Address;
        var kind = IsConsumption ? "" : $" [{TypeOfMp}]";
        return address.Length == 0 ? MeteringPointId + kind : $"{MeteringPointId} – {address}{kind}";
    }
}

/// <summary>Envelope of /meteringpoints/meteringpoints.</summary>
internal sealed class MeteringPointsResponse
{
    [JsonPropertyName("result")] public List<MeteringPoint> Result { get; set; } = new();
}
