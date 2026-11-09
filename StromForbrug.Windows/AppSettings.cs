using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eloverblik;

namespace StromForbrug.Windows;

/// <summary>
/// The user's settings, kept in %APPDATA%\StromForbrug\settings.json between runs. The
/// Eloverblik tokens are encrypted with DPAPI, so the file is useless to anyone but this
/// Windows user on this machine.
/// </summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StromForbrug", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] Entropy = "StromForbrug.Eloverblik"u8.ToArray();

    public string? MeteringPointId { get; set; }

    /// <summary>DPAPI-protected refresh token, base64.</summary>
    public string? ProtectedRefreshToken { get; set; }

    /// <summary>DPAPI-protected cached access token (JSON), base64. It lasts a day.</summary>
    public string? ProtectedAccessToken { get; set; }

    public string? SupplierId { get; set; }
    public string? CustomerGroupId { get; set; }
    // Companies have no id in the API, so they are remembered by name.
    public string? CompanyName { get; set; }
    public string? ProductId { get; set; }
    public bool IncludeVat { get; set; } = true;
    public Resolution Resolution { get; set; } = Resolution.Hour;
    public string? ExportFolder { get; set; }

    [JsonIgnore]
    public string? RefreshToken
    {
        get => Unprotect(ProtectedRefreshToken);
        set => ProtectedRefreshToken = Protect(value);
    }

    [JsonIgnore]
    public AccessToken? AccessToken
    {
        get
        {
            try
            {
                return Unprotect(ProtectedAccessToken) is { } json ? JsonSerializer.Deserialize<AccessToken>(json) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        set => ProtectedAccessToken = Protect(value is null ? null : JsonSerializer.Serialize(value));
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or corrupt file just means starting from the defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering the settings is not worth interrupting the user over.
        }
    }

    private static string? Protect(string? plain) =>
        string.IsNullOrEmpty(plain)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
            return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Another user or machine: the token has to be entered again.
            return null;
        }
    }
}
