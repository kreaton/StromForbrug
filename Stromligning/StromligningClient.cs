using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Stromligning;

/// <summary>Raised when the API answers with a non-success status (or an empty body); carries the server's message.</summary>
public sealed class StromligningApiException : Exception
{
    public StromligningApiException(string message, HttpStatusCode? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status, or null when the request itself succeeded but the body was empty.</summary>
    public HttpStatusCode? StatusCode { get; }
}

/// <summary>Thin client over the public https://stromligning.dk/api endpoints.</summary>
public sealed class StromligningClient : IDisposable
{
    private const string BaseUrl = "https://stromligning.dk/api";
    public const string DefaultUserAgent = "Stromligning/1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ProductInfoHeaderValue _userAgent;

    /// <summary>
    /// Creates a client. Pass an <see cref="HttpClient"/> to share one (for example from
    /// IHttpClientFactory) - it is then left untouched and not disposed; otherwise the client
    /// creates and owns its own with a 30 second timeout.
    /// </summary>
    /// <param name="http">Optional shared HttpClient.</param>
    /// <param name="userAgent">Sent as User-Agent, in "product/version" form.</param>
    public StromligningClient(HttpClient? http = null, string userAgent = DefaultUserAgent)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _userAgent = ProductInfoHeaderValue.Parse(userAgent);
    }

    /// <summary>Grid companies (netselskaber). The chosen one determines the distribution tariff.</summary>
    public Task<List<Supplier>> GetSuppliersAsync(CancellationToken ct) =>
        GetAsync<List<Supplier>>($"{BaseUrl}/suppliers", ct);

    /// <summary>Trading companies (elhandelsselskaber) and their products.</summary>
    public Task<List<Company>> GetCompaniesAsync(CancellationToken ct) =>
        GetAsync<List<Company>>($"{BaseUrl}/companies", ct);

    /// <summary>
    /// Fetches prices for the window [from, to). The API's own default window is
    /// yesterday..tomorrow, so an explicit range is what gets us the coming 24 hours.
    /// Past windows work too, which is what a consumption history needs. The optional
    /// <c>aggregation</c> (e.g. "1h") averages server-side: a month of hourly rows instead of
    /// four times as many 15-minute ones. Null leaves the API's native resolution.
    /// </summary>
    public Task<PriceResponse> GetPricesAsync(
        string supplierId,
        string? customerGroupId,
        string? productId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct,
        string? aggregation = null)
    {
        var query = new List<string>
        {
            "supplierId=" + Uri.EscapeDataString(supplierId),
            "from=" + Uri.EscapeDataString(FormatUtc(from)),
            // The API treats "to" as inclusive of the interval starting at that instant, so we
            // step back a second to land on exactly 24 hours of data rather than one extra slot.
            "to=" + Uri.EscapeDataString(FormatUtc(to.AddSeconds(-1)))
        };

        if (!string.IsNullOrWhiteSpace(customerGroupId))
            query.Add("customerGroupId=" + Uri.EscapeDataString(customerGroupId));

        if (!string.IsNullOrWhiteSpace(productId))
            query.Add("productId=" + Uri.EscapeDataString(productId));

        if (!string.IsNullOrWhiteSpace(aggregation))
            query.Add("aggregation=" + Uri.EscapeDataString(aggregation));

        return GetAsync<PriceResponse>($"{BaseUrl}/prices?{string.Join("&", query)}", ct);
    }

    /// <summary>
    /// The latest hourly spot price forecast for a price area (DK1/DK2), about a week ahead.
    /// Only the spot price is forecast - no tariffs, taxes or surcharge.
    /// </summary>
    public Task<ForecastResponse> GetForecastAsync(string priceArea, CancellationToken ct) =>
        GetAsync<ForecastResponse>(
            $"{BaseUrl}/forecasts/latest?priceArea={Uri.EscapeDataString(priceArea)}", ct);

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        // Headers go on the request rather than DefaultRequestHeaders, so a shared HttpClient
        // is not modified.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(_userAgent);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new StromligningApiException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}: {ExtractError(body)}",
                response.StatusCode);

        var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
        return result ?? throw new StromligningApiException("Empty response from the API.");
    }

    /// <summary>Pulls the "error" field out of an error body, falling back to the raw text.</summary>
    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(empty response)";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through and show whatever came back.
        }

        return body.Length > 300 ? body[..300] + "..." : body;
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
