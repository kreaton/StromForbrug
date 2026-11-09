using System.Globalization;
using System.Text;

namespace StromForbrug;

/// <summary>Writes consumption rows as CSV: one header line, then one line per hour or day.</summary>
public static class ConsumptionExporter
{
    private static readonly string[] Header =
    [
        "start", "end", "kwh", "estimated", "price_kr_per_kwh",
        "spot_kr", "surcharge_kr", "net_tariff_kr", "system_tariff_kr", "distribution_kr", "electricity_tax_kr",
        "total_ex_vat_kr", "vat_kr", "total_kr"
    ];

    /// <summary>
    /// Times are Danish wall-clock time. Cost components are ex. VAT; "total_kr" and
    /// "price_kr_per_kwh" include VAT. Pass da-DK to get ";" and decimal commas for a Danish Excel.
    /// </summary>
    public static void WriteCsv(IEnumerable<ConsumptionRow> rows, TextWriter writer, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        var sep = culture.TextInfo.ListSeparator;
        writer.WriteLine(string.Join(sep, Header));

        foreach (var row in rows)
        {
            var c = row.Cost;
            writer.WriteLine(string.Join(sep,
                Time(row.Start),
                Time(row.End),
                Number(row.Kwh),
                row.IsEstimated ? "1" : "0",
                Number(row.AveragePrice?.Total),
                Number(c?.Electricity.ExVat),
                Number(c?.Surcharge.ExVat),
                Number(c?.NetTariff.ExVat),
                Number(c?.SystemTariff.ExVat),
                Number(c?.Distribution.ExVat),
                Number(c?.ElectricityTax.ExVat),
                Number(c?.Total.ExVat),
                Number(c?.Total.Vat),
                Number(c?.Total.Total)));
        }

        string Number(double? value) => value?.ToString("0.######", culture) ?? "";
    }

    public static string ToCsv(IEnumerable<ConsumptionRow> rows, CultureInfo? culture = null)
    {
        using var writer = new StringWriter();
        WriteCsv(rows, writer, culture);
        return writer.ToString();
    }

    public static async Task SaveCsvAsync(IEnumerable<ConsumptionRow> rows, string path, CultureInfo? culture = null)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        WriteCsv(rows, writer, culture);
    }

    private static string Time(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
}
