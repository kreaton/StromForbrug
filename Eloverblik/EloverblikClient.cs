using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Eloverblik;

/// <summary>Raised for a non-success answer from Eloverblik; carries the HTTP status when there was one.</summary>
public sealed class EloverblikApiException : Exception
{
    public EloverblikApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null,
        bool notEloverblik = false)
        : base(message, inner)
    {
        StatusCode = statusCode;
        IsNotEloverblik = notEloverblik;
    }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// The answer cannot have come from Eloverblik (the token call gave 404 or something that is
    /// not JSON). With a relay as base address, the host is not running it, e.g. no PHP.
    /// </summary>
    public bool IsNotEloverblik { get; }

    /// <summary>
    /// No readable answer: the server could not be reached, or (in a browser calling Eloverblik
    /// directly) the answer lacked the CORS header. Eloverblik's successful answers lack it, so a
    /// browser app must go through a relay; then this means the relay could not be reached.
    /// </summary>
    public bool IsUnreadable => StatusCode is null && InnerException is HttpRequestException;

    /// <summary>The refresh token was rejected: wrong, revoked or expired (they last at most a year).</summary>
    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    /// <summary>Eloverblik is throttling (429) or overloaded (503); trying again later usually works.</summary>
    public bool IsBusy => StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

/// <summary>
/// Thin client over the Eloverblik customer API (https://api.eloverblik.dk/customerapi). Auth is
/// two-step: the long-lived refresh token the customer creates on eloverblik.dk is exchanged at
/// /token for a data access token valid about 24 hours, which then authorises the data calls.
/// The access token is cached and renewed by the client; a 401 on a data call renews it once.
/// </summary>
public sealed class EloverblikClient : IDisposable
{
    public const string DefaultBaseUrl = "https://api.eloverblik.dk/customerapi/api";
    public const string DefaultUserAgent = "Eloverblik/1.0";

    /// <summary>Access tokens live 24 hours; renew an hour early so one never expires mid-call.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(23);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _baseUrl;
    private readonly bool _viaRelay;
    private readonly ProductInfoHeaderValue _userAgent;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _refreshToken;

    /// <param name="http">Optional shared HttpClient; it is then neither modified nor disposed.</param>
    /// <param name="userAgent">Sent as User-Agent, in "product/version" form.</param>
    /// <param name="baseUrl">
    /// Absolute address the API paths (token, meteringpoints/..., meterdata/...) are appended to.
    /// Defaults to Eloverblik itself; a browser app passes its relay instead, since browsers may
    /// not read Eloverblik's answers directly.
    /// </param>
    public EloverblikClient(HttpClient? http = null, string userAgent = DefaultUserAgent, string? baseUrl = null)
    {
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl).TrimEnd('/');
        _viaRelay = !string.Equals(_baseUrl, DefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
        _ownsHttp = http is null;
        // Eloverblik can be slow, especially for longer periods.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _userAgent = ProductInfoHeaderValue.Parse(userAgent);
    }

    /// <summary>The customer's refresh token from eloverblik.dk. Changing it drops a cached access token made from another one.</summary>
    public string? RefreshToken
    {
        get => _refreshToken;
        set
        {
            _refreshToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (AccessToken is { } t && t.RefreshTokenKey != Fingerprint(_refreshToken))
                AccessToken = null;
        }
    }

    /// <summary>
    /// The cached access token. Set it to restore one persisted from an earlier run; it is
    /// ignored unless it belongs to the current <see cref="RefreshToken"/> and has not expired.
    /// </summary>
    public AccessToken? AccessToken { get; set; }

    /// <summary>Raised after a new access token was issued, so the caller can persist it.</summary>
    public event Action<AccessToken>? AccessTokenIssued;

    /// <summary>
    /// A short, non-secret key telling refresh tokens apart (FNV-1a over the text), so a persisted
    /// access token can be matched to the refresh token it was made from.
    /// </summary>
    public static string Fingerprint(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return "";
        var hash = 14695981039346656037UL;
        foreach (var ch in refreshToken)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>Metering points (målepunkter) the refresh token has access to.</summary>
    public async Task<List<MeteringPoint>> GetMeteringPointsAsync(CancellationToken ct)
    {
        var body = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl}/meteringpoints/meteringpoints?includeAll=false"), ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<MeteringPointsResponse>(body, JsonOptions)?.Result ?? new();
    }

    /// <summary>
    /// Meter data for the Danish calendar days [from, to). Data normally reaches up to
    /// yesterday, sometimes a day or two less; days without data are simply missing.
    /// </summary>
    public async Task<List<MeterReading>> GetTimeSeriesAsync(string meteringPointId, DateOnly from, DateOnly to,
        Aggregation aggregation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(meteringPointId))
            throw new ArgumentException("A metering point id is required.", nameof(meteringPointId));
        if (to <= from)
            throw new ArgumentException("'to' must be after 'from'.", nameof(to));

        var url = $"{_baseUrl}/meterdata/gettimeseries/{Day(from)}/{Day(to)}/{aggregation}";
        var payload = JsonSerializer.Serialize(new
        {
            meteringPoints = new { meteringPoint = new[] { meteringPointId.Trim() } }
        });

        var body = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }, ct).ConfigureAwait(false);

        return TimeSeriesParser.Parse(body);
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Sends with the access token; on a 401 the token is renewed and the request repeated once.</summary>
    private async Task<string> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(forceNew: attempt > 0, ct).ConfigureAwait(false);

            using var request = createRequest();
            Authorize(request, token);
            request.Headers.UserAgent.Add(_userAgent);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await SendRawAsync(request, "the data request", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                continue;

            if (!response.IsSuccessStatusCode)
                throw Error(response, body);

            return body;
        }
    }

    /// <summary>Header that repeats the bearer token for a relay (see <see cref="Authorize"/>).</summary>
    public const string RelayAuthorizationHeader = "X-Eloverblik-Authorization";

    /// <summary>
    /// Sets the bearer token. Through a relay it is repeated in <see cref="RelayAuthorizationHeader"/>,
    /// because many Apache/PHP hosts drop the Authorization header before the script sees it.
    /// </summary>
    private void Authorize(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (_viaRelay)
            request.Headers.TryAddWithoutValidation(RelayAuthorizationHeader, "Bearer " + token);
    }

    /// <summary>
    /// Sends, turning "no readable answer" into an <see cref="EloverblikApiException"/> that says
    /// which step failed (<see cref="EloverblikApiException.IsUnreadable"/>).
    /// </summary>
    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, string step, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new EloverblikApiException($"No readable answer from Eloverblik during {step}: {ex.Message}", null, ex);
        }
    }

    private async Task<string> GetAccessTokenAsync(bool forceNew, CancellationToken ct)
    {
        if (_refreshToken is null)
            throw new EloverblikApiException("No Eloverblik refresh token has been set.");

        var key = Fingerprint(_refreshToken);
        if (!forceNew && IsUsable(AccessToken, key))
            return AccessToken!.Value;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceNew && IsUsable(AccessToken, key))
                return AccessToken!.Value;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/token");
            Authorize(request, _refreshToken);
            request.Headers.UserAgent.Add(_userAgent);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await SendRawAsync(request, "the token exchange", ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // Eloverblik's /token never answers 404, and always answers JSON: anything else came
            // from a host that is not relaying (e.g. a static host without PHP).
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new EloverblikApiException($"404 Not Found at {_baseUrl}/token – not Eloverblik or a working relay.",
                    response.StatusCode, notEloverblik: true);
            if (!response.IsSuccessStatusCode)
                throw Error(response, body, tokenCall: true);

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new EloverblikApiException($"The answer from {_baseUrl}/token was not JSON – not Eloverblik or a working relay.",
                    response.StatusCode, ex, notEloverblik: true);
            }

            using var _ = doc;
            var value = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("result", out var r)
                ? r.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
                throw new EloverblikApiException("The token response did not contain a token.");

            AccessToken = new AccessToken(value, DateTimeOffset.UtcNow + AccessTokenLifetime, key);
            AccessTokenIssued?.Invoke(AccessToken);
            return value;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static bool IsUsable(AccessToken? token, string key) =>
        token is not null && token.RefreshTokenKey == key && token.ExpiresAt > DateTimeOffset.UtcNow;

    private static EloverblikApiException Error(HttpResponseMessage response, string body, bool tokenCall = false)
    {
        var detail = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized when tokenCall => "the refresh token was rejected (wrong, revoked or expired)",
            HttpStatusCode.TooManyRequests => "too many requests – Eloverblik limits how often data can be fetched",
            HttpStatusCode.ServiceUnavailable => "Eloverblik is busy – try again in a few minutes",
            _ => ExtractError(body)
        };
        return new EloverblikApiException($"{(int)response.StatusCode} {response.ReasonPhrase}: {detail}", response.StatusCode);
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(empty response)";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "errorText", "error", "message", "title" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String)
                        return e.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON - show whatever came back.
        }

        return body.Length > 300 ? body[..300] + "..." : body;
    }

    public void Dispose()
    {
        _tokenLock.Dispose();
        if (_ownsHttp)
            _http.Dispose();
    }
}
