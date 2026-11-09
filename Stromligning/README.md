# Stromligning

A .NET 10 class library for the public API at <https://stromligning.dk/api>. Use it to get Danish
household electricity prices with the full breakdown (spot price, surcharge, tariffs,
electricity tax, VAT), to extend them about a week ahead with a spot price forecast, and to
save the result as CSV or JSON.

It has no UI or Windows dependencies (plain `net10.0`), so it works from a console tool, a
web API, a background service, a desktop app or the browser (Blazor WebAssembly). The
[StromPriser](../StromPriser/README.md) WinForms app and the [ElForecast](../ElForecast/README.md) Blazor app
in this repository both use it.

## Quick start

```csharp
using Stromligning;

using var client = new StromligningClient();
var service = new PriceService(client);

var result = await service.GetPricesAsync(
    new PriceQuery(SupplierId: "n1_c", CustomerGroupId: "c") { IncludeForecast = true },
    CancellationToken.None);

if (result.Summary is { } s)
    Console.WriteLine($"Cheapest: {s.Cheapest.Start:g} at {s.Cheapest.Total.Total:F4} kr/kWh");

await PriceExporter.SaveAsync(result, "prices.csv");
await PriceExporter.SaveAsync(result, "prices.json");
```

Reference it from another project in the same repository:

```xml
<ProjectReference Include="..\Stromligning\Stromligning.csproj" />
```

To use it from another repository, run `dotnet pack` and publish the package to a NuGet feed
(a local folder or GitHub Packages). The project already has package metadata and
includes this README in the package.

## Overview

| Type | Purpose |
|---|---|
| `StromligningClient` | Raw API access, one method per endpoint. |
| `PriceService` | Fetches prices from now onwards and returns rows ready to display, with an optional forecast and a summary. |
| `PriceQuery` / `PriceResult` / `PriceSummary` | Input and output of `PriceService`. |
| `PriceRow` | One priced period, flattened into its components, plus `ToHourly` and `FromForecast`. |
| `Money` | A component as `ExVat`, `Vat` and `Total` (kr/kWh). |
| `PriceExporter` | Writes a `PriceResult` to CSV or JSON. |
| `Supplier`, `CustomerGroup`, `Company`, `Product`, `PriceResponse`, `ForecastResponse`, … | API models. |
| `StromligningApiException` | Error response from the API. |

## Concepts

- **Supplier (netselskab):** the grid company. It sets the distribution tariff and the price area
  (`DK1` west / `DK2` east). Each supplier has customer groups, one of which is the default.
- **Company (elhandelsselskab):** the trading company. Its product sets the surcharge (*tillæg*).
  Companies have no id in the API, only a name. `Company.SpotProducts` leaves out fixed-price
  products, because those can't be priced per hour.
- **Components:** spot price, surcharge, net tariff, system tariff, distribution and
  electricity tax. Each one is split into ex. VAT, VAT and total. `Total` is the sum of all of
  them.
- **Times:** `PriceRow.Start` and `End` are Danish wall-clock time (`DateTime`), matching
  stromligning.dk. `PriceResult.From` and `To` are `DateTimeOffset`.

## PriceService

```csharp
Task<PriceResult> GetPricesAsync(PriceQuery query, CancellationToken ct, DateTimeOffset? now = null)
```

- **Window:** starts at the top of the current hour, so the period in progress is included.
  It runs for 24 hours (`DefaultWindow`), or 8 days (`ForecastWindow`) when the forecast is on.
  Prices that haven't been published yet are simply missing. For example, before about 13:00
  the window without the forecast ends at midnight.
- **`PriceQuery.Hourly`** (default `true`): averages the 15-minute periods into one row per
  clock hour.
- **`PriceQuery.IncludeForecast`:** fetches the forecast for the response's price area and
  appends estimated rows after the last published price. Published prices always take
  precedence, and there is no gap or overlap where the forecast takes over.
- **Forecast failures:** they don't fail the call. The published rows are returned and the error is in
  `PriceResult.ForecastError`. Cancelling through `ct` still throws.
- **`PriceResult.Summary`:** cheapest, most expensive and average total, plus `Saving` and
  `SavingFraction`. It is `null` when there are no rows.
- **`now`:** pins the clock, for example in tests.

### How forecast rows are estimated

The forecast only gives the **spot price ex. VAT**. For each forecast hour,
`PriceRow.FromForecast` adds 25 % VAT (`PriceRow.VatRate`). It copies every other component
from the latest published row at the **same hour of day**, because tariffs follow the clock.
Forecast rows have `IsForecast = true`.

The estimate doesn't account for tariffs that differ between weekdays and weekends or across
a seasonal change. It also treats surcharges that depend on the spot price as fixed.

## StromligningClient

```csharp
new StromligningClient(HttpClient? http = null, string userAgent = "Stromligning/1.0")
```

- With no `HttpClient`, the client creates its own with a 30-second timeout and disposes it.
- If you pass one (for example from `IHttpClientFactory`), the client doesn't modify or dispose
  it. The headers are set on each request instead of on `DefaultRequestHeaders`.
- Every call sends `User-Agent` and `Accept: application/json`. JSON is read with
  `System.Text.Json`, ignoring the case of property names.

| Method | Endpoint | Notes |
|---|---|---|
| `GetSuppliersAsync(ct)` | `GET /api/suppliers` | Grid companies with price area and customer groups. |
| `GetCompaniesAsync(ct)` | `GET /api/companies` | Trading companies and their products. |
| `GetPricesAsync(supplierId, customerGroupId, productId, from, to, ct)` | `GET /api/prices` | Prices in `[from, to)` with the full breakdown. Leave `customerGroupId` or `productId` as null or blank to omit them. Without a product there is no surcharge. |
| `GetForecastAsync(priceArea, ct)` | `GET /api/forecasts/latest?priceArea=…` | Hourly forecast of the spot price ex. VAT, about 7 days ahead. |

Details of the API's behaviour:

- **Explicit window.** Without `from` and `to`, `/api/prices` defaults to yesterday through
  tomorrow, so the client always sends a range. The times are sent as UTC
  (`yyyy-MM-ddTHH:mm:ssZ`).
- **`to` is sent one second early.** The API includes the period that *starts* at `to`.
- **Asking far ahead is safe.** The response just ends at the last published price.
- **Resolution.** Each price point carries a `resolution` such as `15m` or `1h`. Current data
  comes in 15-minute periods.
- **Forecast price area.** `priceArea` is required: leaving it out gives a 400, and an unknown
  area gives a 404. The forecast's first day usually repeats the day-ahead prices that have
  already been published.

### Errors

| Situation | Thrown |
|---|---|
| Non-success HTTP status | `StromligningApiException`. `StatusCode` holds the status, and `Message` is `"<code> <reason>: <message>"`. The message comes from the `error` field of a JSON body if there is one, otherwise the raw body (cut to 300 characters). |
| Success, but the body deserializes to `null` | `StromligningApiException` with a `null` `StatusCode`. |
| Network failure | `HttpRequestException` |
| `HttpClient` timeout | `TaskCanceledException` |
| Cancellation via `ct` | `OperationCanceledException` |
| Malformed JSON | `JsonException` |

## PriceExporter

```csharp
Task SaveAsync(PriceResult result, string path, ExportFormat? format = null, CultureInfo? culture = null, CancellationToken ct = default)
void WriteCsv(PriceResult result, TextWriter writer, CultureInfo? culture = null)
Task WriteJsonAsync(PriceResult result, Stream stream, CancellationToken ct = default)
string ToCsv(PriceResult result, CultureInfo? culture = null)
string ToJson(PriceResult result)
```

`SaveAsync` picks the format from the file extension (`.json`, otherwise CSV) unless you pass
`format`. Files are written as UTF-8 without a BOM. All prices are in kr/kWh.

### CSV

There is one header line, then one line per period. Times are local, in the form
`yyyy-MM-ddTHH:mm`. Components are ex. VAT. `is_forecast` is `1` or `0`.

```
start,end,is_forecast,spot_ex_vat,surcharge_ex_vat,net_tariff_ex_vat,system_tariff_ex_vat,distribution_ex_vat,electricity_tax_ex_vat,total_ex_vat,vat,total
2026-10-30T08:00,2026-10-30T09:00,0,2.415779,0,0.043,0.072,0.263563,0.008,2.802342,0.700585,3.502927
```

By default the CSV uses `.` for decimals and `,` between fields, which suits other programs.
Pass a culture to use its separators instead. With `da-DK` the file uses `;` and decimal
commas, which a Danish Excel opens straight into columns:

```
start;end;is_forecast;spot_ex_vat;...
2026-10-30T08:00;2026-10-30T09:00;0;2,415779;...
```

### JSON

The JSON file is self-contained: it includes the query, the window, forecast metadata, the
summary, and each row with every component as `{ exVat, vat, total }`.

```json
{
  "exportedAt": "2026-10-30T08:58:26+01:00",
  "unit": "kr/kWh",
  "query": { "supplierId": "n1_c", "customerGroupId": "c", "productId": null, "hourly": true, "includeForecast": true },
  "priceArea": "DK1",
  "from": "2026-10-30T08:00:00+01:00",
  "to": "2026-11-07T08:00:00+01:00",
  "forecastGeneratedAt": "2026-10-30T06:01:08.561+00:00",
  "forecastError": null,
  "summary": { "cheapestStart": "2026-10-31T13:00:00", "cheapestTotal": 2.0023, "priciestStart": "…", "priciestTotal": 4.3801, "averageTotal": 2.8206, "saving": 2.3778 },
  "rows": [
    {
      "start": "2026-10-31T00:00:00", "end": "2026-10-31T01:00:00", "isForecast": true,
      "total": { "exVat": 2.1351, "vat": 0.5338, "total": 2.6688 },
      "spot": { … }, "surcharge": { … }, "netTariff": { … }, "systemTariff": { … },
      "distribution": { … }, "electricityTax": { … }
    }
  ]
}
```

The export uses its own record types, separate from the in-memory types, so renaming a
property in the library doesn't silently change the file format.

## Source files

| File | Contents |
|---|---|
| `StromligningClient.cs` | `StromligningClient`, `StromligningApiException` |
| `Models.cs` | API models and `Money` |
| `PriceRow.cs` | `PriceRow`: `From`, `ToHourly`, `FromForecast` |
| `PriceService.cs` | `PriceService`, `PriceQuery`, `PriceResult`, `PriceSummary` |
| `PriceExporter.cs` | `PriceExporter`, `ExportFormat` |

## Adding an endpoint

1. Add a model to `Models.cs` with `[JsonPropertyName]` attributes.
2. Add a method to `StromligningClient` that builds the URL, escaping every value with
   `Uri.EscapeDataString`, and calls the private `GetAsync<T>`. That method handles headers,
   status codes, error messages and deserialization.
