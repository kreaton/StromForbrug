# Eloverblik

A .NET 10 client for the Eloverblik customer API (https://api.eloverblik.dk/customerapi): token
exchange, metering points and meter data. See the repository README for how the API behaves.

```csharp
using var client = new EloverblikClient { RefreshToken = "<token from eloverblik.dk>" };
var points = await client.GetMeteringPointsAsync(ct);
var readings = await client.GetTimeSeriesAsync(points[0].MeteringPointId,
    new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 8), Aggregation.Hour, ct);
```

Persist `client.AccessToken` (or handle `AccessTokenIssued`) and set it again on the next run to
avoid calling the rate-limited token endpoint every time.
