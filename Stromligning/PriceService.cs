using System.Text.Json;

namespace Stromligning;

/// <summary>What to fetch: the customer's companies plus how the result should be shaped.</summary>
/// <param name="SupplierId">Grid company (netselskab) id. Required.</param>
/// <param name="CustomerGroupId">Optional customer group within the grid company.</param>
/// <param name="ProductId">Optional trading company product; without it there is no surcharge.</param>
public sealed record PriceQuery(string SupplierId, string? CustomerGroupId = null, string? ProductId = null)
{
    /// <summary>Average sub-hourly periods into one row per clock hour.</summary>
    public bool Hourly { get; init; } = true;

    /// <summary>Append forecast-based estimates after the last published price.</summary>
    public bool IncludeForecast { get; init; }
}

/// <summary>Cheapest, most expensive and average total price (incl. VAT) over a set of rows.</summary>
public sealed record PriceSummary(PriceRow Cheapest, PriceRow Priciest, double AverageTotal)
{
    /// <summary>kr/kWh saved by using the cheapest period instead of the priciest.</summary>
    public double Saving => Priciest.Total.Total - Cheapest.Total.Total;

    /// <summary><see cref="Saving"/> as a fraction of the priciest total (0..1).</summary>
    public double SavingFraction => Priciest.Total.Total == 0 ? 0 : Saving / Priciest.Total.Total;

    public static PriceSummary? From(IReadOnlyCollection<PriceRow> rows) =>
        rows.Count == 0
            ? null
            : new PriceSummary(
                rows.MinBy(r => r.Total.Total)!,
                rows.MaxBy(r => r.Total.Total)!,
                rows.Average(r => r.Total.Total));
}

/// <summary>The outcome of <see cref="PriceService.GetPricesAsync"/>.</summary>
public sealed class PriceResult
{
    public required PriceQuery Query { get; init; }
    public string? PriceArea { get; init; }

    /// <summary>The requested window; rows only cover what has been published (or forecast).</summary>
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Published prices in time order, followed by forecast rows when requested.</summary>
    public required IReadOnlyList<PriceRow> Rows { get; init; }

    /// <summary>Null when there are no rows.</summary>
    public PriceSummary? Summary { get; init; }

    public DateTimeOffset? ForecastGeneratedAt { get; init; }

    /// <summary>
    /// Set when the forecast was requested but could not be fetched. The published prices are
    /// still returned - a failed forecast should not cost the caller the real prices.
    /// </summary>
    public Exception? ForecastError { get; init; }

    public int ForecastCount => Rows.Count(r => r.IsForecast);
}

/// <summary>
/// Fetches prices for "now and onwards" and turns them into display-ready rows: the time
/// window, hourly averaging, merging in the forecast and the summary all live here, so a UI
/// only has to show the result.
/// </summary>
public sealed class PriceService
{
    /// <summary>Window without forecast.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Window with forecast. The forecast reaches about 7 days ahead; asking /api/prices for
    /// the same span is harmless because it simply returns what has been published.
    /// </summary>
    public static readonly TimeSpan ForecastWindow = TimeSpan.FromDays(8);

    private readonly StromligningClient _client;

    public PriceService(StromligningClient client) => _client = client;

    /// <summary>
    /// Prices from the top of the current hour - so the period in progress is included - for
    /// <see cref="DefaultWindow"/>, or <see cref="ForecastWindow"/> when the forecast is on.
    /// </summary>
    /// <param name="query">Which companies, and how to shape the rows.</param>
    /// <param name="ct">Cancels the whole operation (a forecast timeout does not).</param>
    /// <param name="now">Overrides the current time; mainly for tests.</param>
    public async Task<PriceResult> GetPricesAsync(PriceQuery query, CancellationToken ct,
        DateTimeOffset? now = null)
    {
        var from = StartOfHour(now ?? DateTimeOffset.Now);
        var to = from + (query.IncludeForecast ? ForecastWindow : DefaultWindow);

        var response = await _client.GetPricesAsync(
            query.SupplierId, query.CustomerGroupId, query.ProductId, from, to, ct).ConfigureAwait(false);

        var rows = response.Prices.Select(PriceRow.From).OrderBy(r => r.Start).ToList();
        if (query.Hourly)
            rows = PriceRow.ToHourly(rows);

        DateTimeOffset? forecastGeneratedAt = null;
        Exception? forecastError = null;

        if (query.IncludeForecast)
        {
            if (string.IsNullOrWhiteSpace(response.PriceArea))
            {
                forecastError = new StromligningApiException("The price response did not include a price area.");
            }
            else
            {
                try
                {
                    var forecast = await _client.GetForecastAsync(response.PriceArea, ct).ConfigureAwait(false);
                    forecastGeneratedAt = forecast.GeneratedAt;
                    rows.AddRange(PriceRow.FromForecast(rows, forecast.Prices));
                }
                catch (Exception ex) when (ex is HttpRequestException or StromligningApiException or JsonException ||
                                           (ex is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    // The last case is HttpClient's timeout, as opposed to the caller cancelling.
                    forecastError = ex;
                }
            }
        }

        return new PriceResult
        {
            Query = query,
            PriceArea = response.PriceArea,
            From = from,
            To = to,
            Rows = rows,
            Summary = PriceSummary.From(rows),
            ForecastGeneratedAt = forecastGeneratedAt,
            ForecastError = forecastError
        };
    }

    public static DateTimeOffset StartOfHour(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Offset);
}
