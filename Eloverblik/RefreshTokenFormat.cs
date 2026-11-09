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
    /// <summary>
    /// Removes a "Bearer " prefix and every character a token cannot contain: spaces, line breaks
    /// and the invisible characters (zero-width spaces, soft hyphens...) that copying on a phone or
    /// tablet can slip in. What is left is the base64url alphabet and the dots between the parts.
    /// </summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var token = text.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..];
        token = new string(token.Where(IsTokenChar).ToArray());
        return token.Length == 0 ? null : token;
    }

    private static bool IsTokenChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.' or '+' or '/' or '=';

    /// <summary>
    /// False when the token is clearly cut short: a JWT has three dot-separated parts, and its
    /// first part is a small base64url-encoded JSON header. Most of the token is the middle part,
    /// so a token cut anywhere there loses the last dot and fails this. Deliberately lenient:
    /// anything else is left to Eloverblik to judge.
    /// </summary>
    public static bool LooksComplete(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return false;
        var parts = token.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0) && IsJsonObject(parts[0]);
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
