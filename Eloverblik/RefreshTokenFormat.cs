namespace Eloverblik;

/// <summary>
/// Cleans and checks a pasted refresh token before it is sent. Eloverblik's tokens are
/// several thousand characters long; a token that lost a piece when it was copied (easy on a phone or
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

    /// <summary>Real tokens are several thousand characters (3,512 and 3,804 seen).</summary>
    public const int MinimumLength = 1000;

    /// <summary>
    /// False only when the token is clearly too short to be a whole one. Its inner structure is
    /// not checked: real tokens did not match a plain JWT check, so anything else is left to
    /// Eloverblik, whose 500 for an unreadable token becomes <see cref="EloverblikApiException.IsTokenUnreadable"/>.
    /// </summary>
    public static bool LooksComplete(string? token) => token is { Length: >= MinimumLength };
}
