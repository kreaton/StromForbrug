using System.Globalization;

namespace Stromligning;

/// <summary>
/// A single priced interval, flattened from the API's nested shape into the components
/// we actually display. All figures are kr/kWh.
/// </summary>
public sealed class PriceRow
{
    public DateTime Start { get; init; }
    public TimeSpan Duration { get; init; }

    public Money Total { get; init; }
    public Money Electricity { get; init; }      // raw spot price
    public Money Surcharge { get; init; }        // trading company's markup
    public Money SystemTariff { get; init; }     // transmission - systemtarif
    public Money NetTariff { get; init; }        // transmission - nettarif
    public Money Distribution { get; init; }     // local grid company's tariff
    public Money ElectricityTax { get; init; }   // elafgift

    /// <summary>True when the spot price is a forecast rather than a published price.</summary>
    public bool IsForecast { get; init; }

    public DateTime End => Start + Duration;

    /// <summary>Danish VAT (moms), applied to the forecast spot price.</summary>
    public const double VatRate = 0.25;

    public static PriceRow From(PricePoint point)
    {
        var details = point.Details;

        return new PriceRow
        {
            Start = ResolveStart(point),
            Duration = ParseResolution(point.Resolution),
            Total = Money.From(point.Price),
            Electricity = Money.From(details?.Electricity),
            Surcharge = Money.From(details?.Surcharge),
            SystemTariff = Money.From(details?.Transmission?.SystemTariff),
            NetTariff = Money.From(details?.Transmission?.NetTariff),
            Distribution = Money.From(details?.Distribution),
            ElectricityTax = Money.From(details?.ElectricityTax)
        };
    }

    /// <summary>
    /// Prefers the API's "localDate" (already in Danish wall-clock time, so the app shows the
    /// same hours as stromligning.dk) and falls back to converting the UTC timestamp.
    /// </summary>
    private static DateTime ResolveStart(PricePoint point)
    {
        if (!string.IsNullOrWhiteSpace(point.LocalDate) &&
            DateTime.TryParse(point.LocalDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
        {
            return local;
        }

        return point.Date.ToLocalTime().DateTime;
    }

    private static TimeSpan ParseResolution(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution))
            return TimeSpan.FromHours(1);

        var digits = new string(resolution.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return TimeSpan.FromHours(1);

        return resolution[digits.Length..].Trim().ToLowerInvariant() switch
        {
            "m" => TimeSpan.FromMinutes(count),
            "h" => TimeSpan.FromHours(count),
            "d" => TimeSpan.FromDays(count),
            _ => TimeSpan.FromHours(1)
        };
    }

    /// <summary>
    /// Collapses sub-hourly rows into one row per clock hour by averaging every component,
    /// so a 15-minute product can be read as 24 hourly prices.
    /// </summary>
    public static List<PriceRow> ToHourly(IEnumerable<PriceRow> rows)
    {
        return rows
            .GroupBy(r => new DateTime(r.Start.Year, r.Start.Month, r.Start.Day, r.Start.Hour, 0, 0))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var factor = 1.0 / g.Count();
                return new PriceRow
                {
                    Start = g.Key,
                    Duration = TimeSpan.FromHours(1),
                    Total = Sum(g, r => r.Total).Scale(factor),
                    Electricity = Sum(g, r => r.Electricity).Scale(factor),
                    Surcharge = Sum(g, r => r.Surcharge).Scale(factor),
                    SystemTariff = Sum(g, r => r.SystemTariff).Scale(factor),
                    NetTariff = Sum(g, r => r.NetTariff).Scale(factor),
                    Distribution = Sum(g, r => r.Distribution).Scale(factor),
                    ElectricityTax = Sum(g, r => r.ElectricityTax).Scale(factor)
                };
            })
            .ToList();
    }

    /// <summary>
    /// Turns forecast spot prices into estimated rows for the hours after the last known row.
    /// Only the spot price is forecast, so every other component is borrowed from the latest
    /// known row at the same hour of day - tariffs follow the clock, so that is the closest
    /// guess there is.
    /// </summary>
    public static List<PriceRow> FromForecast(IReadOnlyList<PriceRow> known, IEnumerable<ForecastPoint> forecast)
    {
        if (known.Count == 0)
            return new List<PriceRow>();

        var hourly = ToHourly(known);
        var templates = hourly
            .GroupBy(r => r.Start.Hour)
            .ToDictionary(g => g.Key, g => g.Last());
        var after = known.Max(r => r.End);

        return forecast
            .Select(p => (Start: p.Time.ToLocalTime().DateTime, p.Price))
            .Where(p => p.Start >= after)
            .OrderBy(p => p.Start)
            .Select(p =>
            {
                var template = templates.GetValueOrDefault(p.Start.Hour) ?? hourly[^1];
                var electricity = new Money(p.Price, p.Price * VatRate, p.Price * (1 + VatRate));
                return new PriceRow
                {
                    Start = p.Start,
                    Duration = TimeSpan.FromHours(1),
                    IsForecast = true,
                    Total = electricity + template.Surcharge + template.NetTariff + template.SystemTariff +
                            template.Distribution + template.ElectricityTax,
                    Electricity = electricity,
                    Surcharge = template.Surcharge,
                    SystemTariff = template.SystemTariff,
                    NetTariff = template.NetTariff,
                    Distribution = template.Distribution,
                    ElectricityTax = template.ElectricityTax
                };
            })
            .ToList();
    }

    private static Money Sum(IEnumerable<PriceRow> rows, Func<PriceRow, Money> selector) =>
        rows.Aggregate(default(Money), (acc, r) => acc + selector(r));
}
