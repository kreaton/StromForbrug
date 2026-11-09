namespace StromForbrug;

/// <summary>
/// Danish wall-clock time, whatever zone the machine or browser is in. Days and hours are
/// shown the way the electricity bill counts them.
/// </summary>
public static class DanishTime
{
    public static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        // IANA id first (browser, Linux, and Windows with ICU), then the Windows id.
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }
        return TimeZoneInfo.Local;
    }

    public static DateTime ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone).DateTime;

    /// <summary>Midnight at the start of a Danish calendar day, as an instant.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    public static DateOnly Today => DateOnly.FromDateTime(ToLocal(DateTimeOffset.UtcNow));
}
