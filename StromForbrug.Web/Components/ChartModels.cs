namespace StromForbrug.Web.Components;

/// <summary>A series in a stacked bar: its legend label and the CSS class that colours it.</summary>
public sealed record ChartPart(string Label, string Css);

/// <summary>
/// One bar: its time and one value per <see cref="ChartPart"/>, stacked bottom-up.
/// Muted bars (estimated readings, missing prices) are drawn lighter.
/// </summary>
public sealed record ChartBar(DateTime Start, double[] Values, bool Muted = false)
{
    public double Sum => Values.Where(v => v > 0).Sum();
}
