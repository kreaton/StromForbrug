using Stromligning;

namespace StromForbrug;

/// <summary>
/// What some consumption cost, in kroner, split into the same components as the price
/// (each as ex. VAT, VAT and total). It is the hourly price times the kWh of that hour, summed.
/// </summary>
public readonly record struct Cost(
    Money Electricity,
    Money Surcharge,
    Money NetTariff,
    Money SystemTariff,
    Money Distribution,
    Money ElectricityTax,
    Money Total)
{
    public static Cost From(PriceRow price, double kwh) => new(
        price.Electricity.Scale(kwh),
        price.Surcharge.Scale(kwh),
        price.NetTariff.Scale(kwh),
        price.SystemTariff.Scale(kwh),
        price.Distribution.Scale(kwh),
        price.ElectricityTax.Scale(kwh),
        price.Total.Scale(kwh));

    public static Cost operator +(Cost a, Cost b) => new(
        a.Electricity + b.Electricity,
        a.Surcharge + b.Surcharge,
        a.NetTariff + b.NetTariff,
        a.SystemTariff + b.SystemTariff,
        a.Distribution + b.Distribution,
        a.ElectricityTax + b.ElectricityTax,
        a.Total + b.Total);

    public Cost Scale(double factor) => new(
        Electricity.Scale(factor),
        Surcharge.Scale(factor),
        NetTariff.Scale(factor),
        SystemTariff.Scale(factor),
        Distribution.Scale(factor),
        ElectricityTax.Scale(factor),
        Total.Scale(factor));

    /// <summary>The components in display order (bottom of a stacked bar first).</summary>
    public static readonly (string Label, Func<Cost, Money> Get)[] Components =
    [
        ("Spotpris", c => c.Electricity),
        ("Tillæg", c => c.Surcharge),
        ("Nettarif", c => c.NetTariff),
        ("Systemtarif", c => c.SystemTariff),
        ("Distribution", c => c.Distribution),
        ("Elafgift", c => c.ElectricityTax)
    ];
}
