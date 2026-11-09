using System.Text.Json;
using Eloverblik;
using Microsoft.JSInterop;

namespace StromForbrug.Web.Services;

/// <summary>
/// Loads and saves <see cref="AppSetup"/> in localStorage, and keeps Eloverblik's access token
/// there too: it lasts a day, and the token endpoint is rate limited, so a reload should not
/// have to ask for a new one.
/// </summary>
public sealed class SetupStore
{
    private const string Key = "stromforbrug.setup";
    private const string TokenKey = "stromforbrug.accesstoken";

    private readonly IJSRuntime _js;
    private readonly EloverblikClient _eloverblik;
    private AppSetup? _cached;
    private bool _tokenRestored;

    public SetupStore(IJSRuntime js, EloverblikClient eloverblik)
    {
        _js = js;
        _eloverblik = eloverblik;
        _eloverblik.AccessTokenIssued += token => _ = SetAsync(TokenKey, JsonSerializer.Serialize(token));
    }

    public async Task<AppSetup> LoadAsync()
    {
        if (_cached is null)
        {
            try
            {
                var json = await GetAsync(Key);
                if (!string.IsNullOrWhiteSpace(json))
                    _cached = JsonSerializer.Deserialize<AppSetup>(json);
            }
            catch (JsonException)
            {
                // Corrupt storage just means starting from the defaults.
            }

            _cached ??= new AppSetup();
        }

        await ApplyAsync(_cached);
        return _cached.Clone();
    }

    public async Task SaveAsync(AppSetup setup)
    {
        _cached = setup.Clone();
        await ApplyAsync(_cached);
        await SetAsync(Key, JsonSerializer.Serialize(setup));
    }

    /// <summary>Hands the refresh token to the client, plus a stored access token the first time.</summary>
    private async Task ApplyAsync(AppSetup setup)
    {
        if (!_tokenRestored)
        {
            _tokenRestored = true;
            try
            {
                var json = await GetAsync(TokenKey);
                if (!string.IsNullOrWhiteSpace(json))
                    _eloverblik.AccessToken = JsonSerializer.Deserialize<AccessToken>(json);
            }
            catch (JsonException)
            {
            }
        }

        // Drops the access token if it was made from a different refresh token.
        _eloverblik.RefreshToken = setup.RefreshToken;
    }

    // localStorage can be blocked (private windows, site data off); the app then simply
    // forgets between visits, which is not worth interrupting the user over.
    private async Task<string?> GetAsync(string key)
    {
        try
        {
            return await _js.InvokeAsync<string?>("stromForbrug.storageGet", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task SetAsync(string key, string value)
    {
        try
        {
            await _js.InvokeVoidAsync("stromForbrug.storageSet", key, value);
        }
        catch (JSException)
        {
        }
    }
}
