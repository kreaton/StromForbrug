using Stromligning;

namespace StromForbrug;

/// <summary>Hour by hour, or one row per day.</summary>
public enum Resolution
{
    Hour,
    Day
}

/// <summary>
/// Consumption over one hour or one day, with what it cost when a price was known.
/// Times are Danish wall-clock time.
/// </summary>
public sealed class ConsumptionRow
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>Start as an instant; unambiguous also in the repeated hour when summer time ends.</summary>
    public DateTimeOffset StartUtc { get; init; }

    public double Kwh { get; init; }

    /// <summary>Number of hourly readings behind the row (1 for an hour; 23–25 for a full day).</summary>
    public int Hours { get; init; }

    /// <summary>True when at least one reading was estimated rather than measured.</summary>
    public bool IsEstimated => EstimatedHours > 0;

    /// <summary>How many of <see cref="Hours"/> were estimated.</summary>
    public int EstimatedHours { get; init; }

    /// <summary>True when most of the row is estimated; a day with one estimated hour is still a measured day.</summary>
    public bool MostlyEstimated => EstimatedHours * 2 > Hours;

    /// <summary>Null when no hour in the row had a price.</summary>
    public Cost? Cost { get; init; }

    /// <summary>kWh in the hours that had a price; equals <see cref="Kwh"/> unless prices were missing.</summary>
    public double PricedKwh { get; init; }

    public int HoursWithoutPrice { get; init; }

    /// <summary>Average price paid per kWh (cost ÷ priced kWh), or null without a price or consumption.</summary>
    public Money? AveragePrice =>
        Cost is { } c && PricedKwh > 0 ? c.Total.Scale(1 / PricedKwh) : null;

    internal static ConsumptionRow Hour(DateTimeOffset startUtc, TimeSpan duration, double kwh, bool estimated, PriceRow? price) => new()
    {
        Start = DanishTime.ToLocal(startUtc),
        End = DanishTime.ToLocal(startUtc + duration),
        StartUtc = startUtc,
        Kwh = kwh,
        Hours = 1,
        EstimatedHours = estimated ? 1 : 0,
        Cost = price is null ? null : StromForbrug.Cost.From(price, kwh),
        PricedKwh = price is null ? 0 : kwh,
        HoursWithoutPrice = price is null ? 1 : 0
    };

    /// <summary>Sums hourly rows into one row per Danish calendar day.</summary>
    public static List<ConsumptionRow> ToDaily(IEnumerable<ConsumptionRow> hourly) =>
        hourly
            .GroupBy(r => r.Start.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var priced = g.Where(r => r.Cost is not null).ToList();
                return new ConsumptionRow
                {
                    Start = g.Key,
                    End = g.Key.AddDays(1),
                    StartUtc = DanishTime.StartOfDay(DateOnly.FromDateTime(g.Key)),
                    Kwh = g.Sum(r => r.Kwh),
                    Hours = g.Sum(r => r.Hours),
                    EstimatedHours = g.Sum(r => r.EstimatedHours),
                    Cost = priced.Count == 0 ? null : priced.Select(r => r.Cost!.Value).Aggregate((a, b) => a + b),
                    PricedKwh = priced.Sum(r => r.PricedKwh),
                    HoursWithoutPrice = g.Sum(r => r.HoursWithoutPrice)
                };
            })
            .ToList();
}
