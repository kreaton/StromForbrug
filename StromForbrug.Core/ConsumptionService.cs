using System.Text.Json;
using Eloverblik;
using Stromligning;

namespace StromForbrug;

/// <summary>What to fetch.</summary>
/// <param name="MeteringPointId">The 18-digit målepunkt id from Eloverblik.</param>
/// <param name="From">First Danish calendar day, inclusive.</param>
/// <param name="To">Last Danish calendar day, inclusive.</param>
/// <param name="Price">The customer's companies at stromligning.dk; null to skip prices.</param>
public sealed record ConsumptionQuery(string MeteringPointId, DateOnly From, DateOnly To, PriceQuery? Price)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>The outcome of <see cref="ConsumptionService.GetAsync"/>. Holds both resolutions.</summary>
public sealed class ConsumptionResult
{
    public required ConsumptionQuery Query { get; init; }

    /// <summary>One row per hour with data, in time order.</summary>
    public required IReadOnlyList<ConsumptionRow> Hourly { get; init; }

    /// <summary>One row per day with data.</summary>
    public required IReadOnlyList<ConsumptionRow> Daily { get; init; }

    public IReadOnlyList<ConsumptionRow> Rows(Resolution resolution) =>
        resolution == Resolution.Day ? Daily : Hourly;

    public string? PriceArea { get; init; }

    /// <summary>
    /// Set when prices were asked for but could not be fetched. The consumption is still
    /// returned: a failed price lookup should not cost the caller the kWh.
    /// </summary>
    public Exception? PriceError { get; init; }

    public double TotalKwh => Hourly.Sum(r => r.Kwh);
    public double PricedKwh => Hourly.Sum(r => r.PricedKwh);
    public int HoursWithoutPrice => Hourly.Sum(r => r.HoursWithoutPrice);
    public int EstimatedHours => Hourly.Count(r => r.IsEstimated);

    /// <summary>Null when no hour had a price.</summary>
    public Cost? TotalCost =>
        Hourly.Where(r => r.Cost is not null).Select(r => r.Cost!.Value).Aggregate((Cost?)null, (a, c) => a is null ? c : a.Value + c);

    /// <summary>Average price paid per kWh over the whole period.</summary>
    public Money? AveragePrice => TotalCost is { } c && PricedKwh > 0 ? c.Total.Scale(1 / PricedKwh) : null;

    /// <summary>End of the last reading, i.e. how far Eloverblik's data reaches.</summary>
    public DateTime? DataUntil => Hourly.Count == 0 ? null : Hourly[^1].End;

    /// <summary>True when this result holds every day of [from, to] for the same metering point and prices.</summary>
    public bool Covers(ConsumptionQuery query) =>
        query.MeteringPointId == Query.MeteringPointId && query.Price == Query.Price &&
        query.From >= Query.From && query.To <= Query.To;

    /// <summary>The same result narrowed to fewer days, without fetching again.</summary>
    public ConsumptionResult Slice(DateOnly from, DateOnly to)
    {
        var start = DanishTime.StartOfDay(from);
        var end = DanishTime.StartOfDay(to.AddDays(1));
        var hourly = Hourly.Where(r => r.StartUtc >= start && r.StartUtc < end).ToList();
        return new ConsumptionResult
        {
            Query = Query with { From = from, To = to },
            Hourly = hourly,
            Daily = ConsumptionRow.ToDaily(hourly),
            PriceArea = PriceArea,
            PriceError = PriceError
        };
    }
}

/// <summary>
/// Fetches hourly consumption from Eloverblik and hourly prices from stromligning.dk for the
/// same days, and multiplies them hour by hour. Daily totals are sums of those hours, so a day's
/// cost reflects when in the day the power was used, not just the day's average price.
/// </summary>
public sealed class ConsumptionService
{
    /// <summary>The longest period one call accepts.</summary>
    public const int MaxDays = 30;

    private readonly EloverblikClient _eloverblik;
    private readonly StromligningClient _stromligning;

    public ConsumptionService(EloverblikClient eloverblik, StromligningClient stromligning)
    {
        _eloverblik = eloverblik;
        _stromligning = stromligning;
    }

    public async Task<ConsumptionResult> GetAsync(ConsumptionQuery query, CancellationToken ct)
    {
        if (query.Days < 1)
            throw new ArgumentException("The period must be at least one day.", nameof(query));
        if (query.Days > MaxDays)
            throw new ArgumentException($"The period can be at most {MaxDays} days.", nameof(query));

        var fromUtc = DanishTime.StartOfDay(query.From);
        var toUtc = DanishTime.StartOfDay(query.To.AddDays(1));

        // Both calls at once: Eloverblik is the slow one.
        var readingsTask = _eloverblik.GetTimeSeriesAsync(
            query.MeteringPointId, query.From, query.To.AddDays(1), Aggregation.Hour, ct);
        var pricesTask = query.Price is { } pq ? GetHourlyPricesAsync(pq, fromUtc, toUtc, ct) : null;

        var readings = await readingsTask.ConfigureAwait(false);

        Dictionary<DateTimeOffset, PriceRow>? prices = null;
        string? priceArea = null;
        Exception? priceError = null;
        if (pricesTask is not null)
        {
            try
            {
                (prices, priceArea) = await pricesTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or StromligningApiException or JsonException ||
                                       (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                priceError = ex;
            }
        }

        var hourly = ToHourlyReadings(readings)
            .Where(r => r.StartUtc >= fromUtc && r.StartUtc < toUtc)
            .Select(r => ConsumptionRow.Hour(r.StartUtc, r.Duration, r.Kwh, r.IsEstimated,
                prices?.GetValueOrDefault(r.StartUtc)))
            .ToList();

        return new ConsumptionResult
        {
            Query = query,
            Hourly = hourly,
            Daily = ConsumptionRow.ToDaily(hourly),
            PriceArea = priceArea,
            PriceError = priceError
        };
    }

    /// <summary>Hourly data normally comes as is; 15-minute data is summed into clock hours.</summary>
    private static IEnumerable<MeterReading> ToHourlyReadings(IEnumerable<MeterReading> readings) =>
        readings
            .GroupBy(r => TruncateToHour(r.StartUtc))
            .OrderBy(g => g.Key)
            .Select(g => new MeterReading(g.Key, TimeSpan.FromHours(1), g.Sum(r => r.Kwh),
                g.Select(r => r.Quality).FirstOrDefault(q => q is not null and not "A04") ?? g.First().Quality));

    /// <summary>Prices keyed by the UTC start of each hour; 15-minute prices are averaged.</summary>
    private async Task<(Dictionary<DateTimeOffset, PriceRow> Prices, string? PriceArea)> GetHourlyPricesAsync(
        PriceQuery query, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var response = await _stromligning.GetPricesAsync(
            query.SupplierId, query.CustomerGroupId, query.ProductId, from, to, ct, aggregation: "1h").ConfigureAwait(false);

        // Keyed by the UTC instant rather than PriceRow.Start: local time repeats an hour when
        // summer time ends, and averaging by local hour would mix those two hours.
        var prices = response.Prices
            .GroupBy(p => TruncateToHour(p.Date))
            .ToDictionary(g => g.Key, g => Average(g.Select(PriceRow.From).ToList()));

        return (prices, response.PriceArea);
    }

    private static PriceRow Average(IReadOnlyList<PriceRow> rows)
    {
        if (rows.Count == 1)
            return rows[0];

        var f = 1.0 / rows.Count;
        Money Avg(Func<PriceRow, Money> get) => rows.Aggregate(default(Money), (acc, r) => acc + get(r)).Scale(f);
        return new PriceRow
        {
            Start = rows[0].Start,
            Duration = TimeSpan.FromHours(1),
            Total = Avg(r => r.Total),
            Electricity = Avg(r => r.Electricity),
            Surcharge = Avg(r => r.Surcharge),
            SystemTariff = Avg(r => r.SystemTariff),
            NetTariff = Avg(r => r.NetTariff),
            Distribution = Avg(r => r.Distribution),
            ElectricityTax = Avg(r => r.ElectricityTax)
        };
    }

    private static DateTimeOffset TruncateToHour(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
