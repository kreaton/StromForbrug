using System.Globalization;
using System.Reflection;

namespace StromForbrug.Web;

/// <summary>
/// Version, commit and build time stamped in by StromForbrug.Web.csproj; the same scheme and footer
/// format as CheckYourDanish.
/// </summary>
public static class BuildInfo
{
    private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;

    // "1.0.42+a1b2c3d4..." – the part after + is the git commit, when the build had one.
    private static readonly string[] Informational =
        (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+', 2);

    public static string Version { get; } = Informational[0];

    public static string? Commit { get; } =
        Informational.Length > 1 ? Informational[1][..Math.Min(7, Informational[1].Length)] : null;

    public static DateTime? BuildDate { get; } =
        DateTime.TryParse(
            Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value,
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc)
            ? utc
            : null;

    /// <summary>"v1.0.42 · 24.10.2026 14:32 · a1b2c3d", with the time in Danish local time.</summary>
    public static string Display { get; } = string.Join(" · ", new[]
    {
        "v" + Version,
        BuildDate is { } date ? ToDanishTime(date).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : null,
        Commit,
    }.Where(part => part is not null));

    /// <summary>Sent to stromligning.dk and Eloverblik as User-Agent.</summary>
    public static string UserAgent => $"StromForbrug/{Version}";

    private static DateTime ToDanishTime(DateTime utc)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc.ToLocalTime();
        }
    }
}
