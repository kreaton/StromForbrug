# StromForbrug – what did your electricity use cost?

![Blazor WebAssembly](https://img.shields.io/badge/Blazor-WebAssembly-lightgrey)
![.NET 10](https://img.shields.io/badge/.NET-10-lightgrey)
![Windows app](https://img.shields.io/badge/also-Windows%20app-lightgrey)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

See how much electricity your household has used, hour by hour for the past week or day by day
for the past month, and what it cost with everything on the bill: the spot price, your
electricity company's surcharge, the grid tariffs and the electricity tax.

The consumption comes from [Eloverblik](https://eloverblik.dk), the prices from
[stromligning.dk](https://stromligning.dk). The apps themselves are in Danish. It is the
sibling of [StromPriser](https://github.com/kreaton/StromPriser), which looks at prices ahead.

## Two ways to use it

| | **StromForbrug** in the browser | **StromForbrug** on Windows |
|---|---|---|
| Shows | Bar charts of kWh and kroner, and tables | The same charts plus one table with every component |
| Install | Nothing, just open the page. It can also be added to your home screen. | A small Windows program |
| Your token | Stored in the browser (localStorage); sent to Eloverblik through the site's relay, which keeps nothing | Encrypted for your Windows user (DPAPI) in `%APPDATA%\StromForbrug\settings.json` |

## Setup

1. **Eloverblik token.** Log in to [eloverblik.dk](https://eloverblik.dk) with MitID, choose
   *Datadeling* and *Opret token*. Copy the token into **Indstillinger**. It lasts up to a year.
2. **Målepunkt.** Press **Hent målepunkter** to list the metering points the token gives access
   to (your consumption point is picked automatically), or type the 18-digit id from your bill.
3. **Netselskab, kundegruppe, elhandelsselskab, produkt.** The same choices as in StromPriser.
   They decide the tariffs and the surcharge. Leave the grid company empty to see kWh only.
4. **Kroner inkl. moms**: what you actually paid, or ex. VAT.

## Reading it

- **Time for time / Dag for dag** switches between one bar per hour (default: the past 7 days)
  and one bar per day (default: the past 30 days). **Fra** and **Til** pick any period of up to
  30 days. Eloverblik's data normally reaches yesterday, sometimes a day or two less.
- **Forbrug** shows kWh. Hours that Eloverblik marks as estimated, and days that are mostly
  estimated, are drawn lighter.
- **Pris** shows kroner, stacked by what the price is made of. Each hour's cost is that hour's kWh
  times that hour's price, so a day's total reflects *when* you used the power, not the day's
  average price. Subscriptions and other fixed fees are not included.
- The boxes give the total kWh, the total cost, the average price you paid per kWh (weighted by
  your consumption) and the highest hour or day.
- **Vis som tabel** shows the numbers, and **Hent CSV** / **Gem som CSV...** saves them in a
  format that opens straight into columns in a Danish Excel.

## For developers

| Project | |
|---|---|
| `Eloverblik` | Class library for the Eloverblik customer API: token exchange (cached and renewed, with a hook to persist it), metering points, and meter data parsed into `MeterReading`s. |
| `Stromligning` | Copy of the library from StromPriser, plus an optional `aggregation` ("1h") on `GetPricesAsync`. |
| `StromForbrug.Core` | `ConsumptionService` fetches both and multiplies them hour by hour (`ConsumptionRow`, `Cost`, daily totals, `Slice`, CSV export). Danish time via `DanishTime`. |
| `StromForbrug.Web` | The Blazor WebAssembly app. Strømligning is called directly; Eloverblik through the PHP relay `wwwroot/api/eloverblik.php` (see below). |
| `StromForbrug.Windows` | The Windows Forms app (`StromForbrug.exe`) with a small self-drawn bar chart. |

```powershell
tools/run-dev.ps1                           # PHP relay on :5290 + web app on http://localhost:5286
dotnet run --project StromForbrug.Windows   # Windows only
```

### Why the web app needs a PHP relay

A browser only lets a page read another site's answer if the answer carries an
`Access-Control-Allow-Origin` header. Eloverblik's *error* answers have it, its *successful* ones
don't, so the browser throws away the access token and the app sees only `TypeError: Failed to
fetch`. The Windows app is not affected: the rule exists only in browsers.

So the web app sends its Eloverblik calls to `api/eloverblik.php` on its own site, which passes
them on to `api.eloverblik.dk` and returns the answer. It relays only the three endpoints the app
uses (token, metering points, time series), stores and logs nothing, and needs PHP 8 with the curl
extension. `api/.htaccess` makes Apache hand the `Authorization` header on to PHP.

- **Hosting:** the site must run PHP. On a static host such as GitHub Pages the app loads and shows
  prices, but says it cannot reach Eloverblik.
- **Development:** the Blazor dev server does not run PHP. `tools/run-dev.ps1` starts PHP's built-in
  server on port 5290 for the relay, and `wwwroot/appsettings.Development.json` (not published)
  points the app there. The relay lets `localhost` pages read its answers for this purpose.

Notes on the Eloverblik API:

- `GET /customerapi/api/token` with the refresh token as bearer returns `{ "result": "<access token>" }`,
  valid 24 hours. The token endpoint is rate limited, so the apps keep the access token between runs.
- `POST /customerapi/api/meterdata/gettimeseries/{from}/{to}/Hour` with
  `{ "meteringPoints": { "meteringPoint": ["<id>"] } }`. Dates are Danish calendar days, `to` exclusive.
  Each `Period` starts at a UTC instant and its `Point`s are 1-based positions of one `resolution`.
- A 200 answer can still carry a per-point error (`success: false`, `errorText`), which becomes an
  `EloverblikApiException`. 429 and 503 mean "try again later" (`IsBusy`), 401 on the token call
  means the refresh token is wrong or expired (`IsUnauthorized`).

The icons are drawn by `tools/make-icons.ps1`. The workflow in `.github/workflows/deploy-web.yml`
publishes the web app by FTP (with the relay, so that host must run PHP) and to GitHub Pages
(prices only), like ElForecast's.

## License

[MIT](LICENSE) © 2026 Per Hejndorf. Consumption data from [Eloverblik](https://eloverblik.dk),
price data from [stromligning.dk](https://stromligning.dk).
