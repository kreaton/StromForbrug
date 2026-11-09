using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Stromligning;

public enum ExportFormat
{
    Csv,
    Json
}

/// <summary>
/// Writes a <see cref="PriceResult"/> as CSV (one line per period) or JSON (rows plus the
/// query, summary and forecast metadata). All prices are kr/kWh.
/// </summary>
public static class PriceExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] CsvHeader =
    [
        "start", "end", "is_forecast",
        "spot_ex_vat", "surcharge_ex_vat", "net_tariff_ex_vat", "system_tariff_ex_vat",
        "distribution_ex_vat", "electricity_tax_ex_vat",
        "total_ex_vat", "vat", "total"
    ];

    /// <summary>
    /// Saves to <paramref name="path"/>, picking the format from the extension (.json, else CSV)
    /// unless <paramref name="format"/> is given. See <see cref="WriteCsv"/> for <paramref name="culture"/>.
    /// </summary>
    public static async Task SaveAsync(PriceResult result, string path, ExportFormat? format = null,
        CultureInfo? culture = null, CancellationToken ct = default)
    {
        await using var stream = File.Create(path);

        if ((format ?? FormatFromPath(path)) == ExportFormat.Json)
        {
            await WriteJsonAsync(result, stream, ct).ConfigureAwait(false);
            return;
        }

        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        WriteCsv(result, writer, culture);
    }

    public static ExportFormat FormatFromPath(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Json
            : ExportFormat.Csv;

    /// <summary>
    /// One header line, then one line per period. Times are local wall-clock time in ISO form.
    /// By default numbers use "." and fields ","; pass a culture (e.g. da-DK) to use its decimal
    /// and list separators instead, which is what a Danish Excel expects when opening the file.
    /// </summary>
    public static void WriteCsv(PriceResult result, TextWriter writer, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        var separator = culture.TextInfo.ListSeparator;

        writer.WriteLine(string.Join(separator, CsvHeader));

        foreach (var row in result.Rows)
        {
            writer.WriteLine(string.Join(separator,
                FormatTime(row.Start),
                FormatTime(row.End),
                row.IsForecast ? "1" : "0",
                Number(row.Electricity.ExVat),
                Number(row.Surcharge.ExVat),
                Number(row.NetTariff.ExVat),
                Number(row.SystemTariff.ExVat),
                Number(row.Distribution.ExVat),
                Number(row.ElectricityTax.ExVat),
                Number(row.Total.ExVat),
                Number(row.Total.Vat),
                Number(row.Total.Total)));
        }

        string Number(double value) => value.ToString("0.######", culture);
    }

    public static string ToCsv(PriceResult result, CultureInfo? culture = null)
    {
        using var writer = new StringWriter();
        WriteCsv(result, writer, culture);
        return writer.ToString();
    }

    /// <summary>The rows plus enough context (query, window, summary, forecast) to stand on its own.</summary>
    public static Task WriteJsonAsync(PriceResult result, Stream stream, CancellationToken ct = default) =>
        JsonSerializer.SerializeAsync(stream, ToDocument(result), JsonOptions, ct);

    public static string ToJson(PriceResult result) =>
        JsonSerializer.Serialize(ToDocument(result), JsonOptions);

    private static JsonDocumentShape ToDocument(PriceResult result) => new(
        ExportedAt: DateTimeOffset.Now,
        Unit: "kr/kWh",
        Query: result.Query,
        PriceArea: result.PriceArea,
        From: result.From,
        To: result.To,
        ForecastGeneratedAt: result.ForecastGeneratedAt,
        ForecastError: result.ForecastError?.Message,
        Summary: result.Summary is { } s
            ? new SummaryShape(s.Cheapest.Start, s.Cheapest.Total.Total, s.Priciest.Start,
                s.Priciest.Total.Total, s.AverageTotal, s.Saving)
            : null,
        Rows: result.Rows.Select(r => new RowShape(
            r.Start, r.End, r.IsForecast, r.Total, r.Electricity, r.Surcharge, r.NetTariff,
            r.SystemTariff, r.Distribution, r.ElectricityTax)).ToList());

    private static string FormatTime(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    // Dedicated shapes so the file format does not change by accident when the in-memory
    // types do.
    private sealed record JsonDocumentShape(
        DateTimeOffset ExportedAt, string Unit, PriceQuery Query, string? PriceArea,
        DateTimeOffset From, DateTimeOffset To, DateTimeOffset? ForecastGeneratedAt,
        string? ForecastError, SummaryShape? Summary, List<RowShape> Rows);

    private sealed record SummaryShape(
        DateTime CheapestStart, double CheapestTotal, DateTime PriciestStart, double PriciestTotal,
        double AverageTotal, double Saving);

    private sealed record RowShape(
        DateTime Start, DateTime End, bool IsForecast, Money Total, Money Spot, Money Surcharge,
        Money NetTariff, Money SystemTariff, Money Distribution, Money ElectricityTax);
}
