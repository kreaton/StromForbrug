using System.Text;
using System.Text.Json;

namespace Eloverblik;

/// <summary>
/// Cleans and checks a pasted refresh token before it is sent. Eloverblik's tokens are JWTs of
/// roughly 3,500 characters; a token that lost a piece when it was copied (easy on a phone or
/// tablet) makes Eloverblik answer 500 "An error occurred processing your request" rather than 401.
/// </summary>
public static class RefreshTokenFormat
{
    /// <summary>Removes a "Bearer " prefix and every space and line break (a JWT never contains any).</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var token = text.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..];
        token = new string(token.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return token.Length == 0 ? null : token;
    }

    /// <summary>
    /// True when the token has a JWT's shape: three dot-separated parts, of which the first two
    /// are base64url-encoded JSON objects. A token cut short anywhere almost always fails this.
    /// </summary>
    public static bool LooksComplete(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return false;
        var parts = token.Split('.');
        return parts.Length == 3 && parts[2].Length > 0 && IsJsonObject(parts[0]) && IsJsonObject(parts[1]);
    }

    private static bool IsJsonObject(string base64Url)
    {
        try
        {
            var s = base64Url.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(s));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}
