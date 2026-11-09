using System.Globalization;
using System.Text.Json;
using System.Xml;

namespace Eloverblik;

/// <summary>
/// Flattens the CIM-shaped answer of /meterdata/gettimeseries into <see cref="MeterReading"/>s:
/// result[] → MyEnergyData_MarketDocument → TimeSeries[] → Period[] → Point[].
/// </summary>
public static class TimeSeriesParser
{
    /// <summary>Parses a response body. Throws <see cref="EloverblikApiException"/> for a per-point error.</summary>
    public static List<MeterReading> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    public static List<MeterReading> Parse(JsonElement root)
    {
        var readings = new List<MeterReading>();
        if (!root.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            return readings;

        foreach (var result in results.EnumerateArray())
        {
            // Each metering point in the request gets its own result, which can fail on its own
            // (unknown point, no access) while the HTTP status is still 200.
            if (result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            {
                var code = result.TryGetProperty("errorCode", out var c) ? c.ToString() : "?";
                var text = result.TryGetProperty("errorText", out var t) ? t.GetString() : null;
                throw new EloverblikApiException($"Eloverblik error {code}: {text ?? "unknown error"}");
            }

            if (!result.TryGetProperty("MyEnergyData_MarketDocument", out var document) ||
                document.ValueKind != JsonValueKind.Object ||
                !document.TryGetProperty("TimeSeries", out var series) ||
                series.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var timeSeries in series.EnumerateArray())
            {
                if (!timeSeries.TryGetProperty("Period", out var periods) || periods.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var period in periods.EnumerateArray())
                    ParsePeriod(period, readings);
            }
        }

        // Periods can overlap at the edges when a request spans several documents; the last one wins.
        return readings
            .GroupBy(r => r.StartUtc)
            .Select(g => g.Last())
            .OrderBy(r => r.StartUtc)
            .ToList();
    }

    private static void ParsePeriod(JsonElement period, List<MeterReading> readings)
    {
        var start = DateTimeOffset.Parse(
            period.GetProperty("timeInterval").GetProperty("start").GetString()!,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

        var step = period.TryGetProperty("resolution", out var res) && res.GetString() is { Length: > 0 } r
            ? ParseResolution(r)
            : TimeSpan.FromHours(1);

        if (!period.TryGetProperty("Point", out var points) || points.ValueKind != JsonValueKind.Array)
            return;

        foreach (var point in points.EnumerateArray())
        {
            // position is 1-based and comes as a string: position 1 = start, 2 = start + one step...
            var position = ReadInt(point.GetProperty("position"));
            var quantity = ReadDouble(point.GetProperty("out_Quantity.quantity"));
            var quality = point.TryGetProperty("out_Quantity.quality", out var q) ? q.GetString() : null;

            readings.Add(new MeterReading(start + step * (position - 1), step, quantity, quality));
        }
    }

    /// <summary>ISO 8601 durations as used by DataHub: PT15M, PT1H, P1D. Months and years are not fixed lengths and are not supported.</summary>
    public static TimeSpan ParseResolution(string resolution)
    {
        try
        {
            return XmlConvert.ToTimeSpan(resolution);
        }
        catch (FormatException)
        {
            return TimeSpan.FromHours(1);
        }
    }

    private static int ReadInt(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.Parse(e.GetString()!, CultureInfo.InvariantCulture);

    private static double ReadDouble(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.Parse(e.GetString()!, CultureInfo.InvariantCulture);
}
