using System.Text.Json.Serialization;

namespace Stromligning;

/// <summary>A price component split into its ex-VAT, VAT and total parts (kr/kWh).</summary>
public readonly record struct Money(double ExVat, double Vat, double Total)
{
    public static Money From(Amount? a) => a is null ? default : new Money(a.Value, a.Vat, a.Total);

    public static Money operator +(Money a, Money b) =>
        new(a.ExVat + b.ExVat, a.Vat + b.Vat, a.Total + b.Total);

    public Money Scale(double factor) => new(ExVat * factor, Vat * factor, Total * factor);
}

// ---------- /api/suppliers (netselskaber - these drive the distribution tariff) ----------

public sealed class Supplier
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("companyName")] public string? CompanyName { get; set; }
    [JsonPropertyName("priceArea")] public string? PriceArea { get; set; }
    [JsonPropertyName("customerGroups")] public List<CustomerGroup> CustomerGroups { get; set; } = new();

    public override string ToString() =>
        string.IsNullOrWhiteSpace(PriceArea) ? Name : $"{Name}  ({PriceArea})";
}

public sealed class CustomerGroup
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("default")] public bool Default { get; set; }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Description) ? (Name ?? Id) : $"{Name} - {Description}";
}

// ---------- /api/companies (elhandelsselskaber - these drive the surcharge) ----------

public sealed class Company
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("products")] public List<Product> Products { get; set; } = new();

    /// <summary>The products priced from the spot price - the only ones /api/prices can price per hour.</summary>
    [JsonIgnore]
    public IEnumerable<Product> SpotProducts => Products.Where(p => !p.IsFixedPrice);

    public override string ToString() => Name;
}

public sealed class Product
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("productType")] public string? ProductType { get; set; }

    /// <summary>True for products whose price is fixed rather than spot-based.</summary>
    public bool IsFixedPrice =>
        string.Equals(ProductType, "fixed", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

// ---------- /api/prices ----------

public sealed class PriceResponse
{
    [JsonPropertyName("priceArea")] public string? PriceArea { get; set; }
    [JsonPropertyName("prices")] public List<PricePoint> Prices { get; set; } = new();
}

public sealed class PricePoint
{
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("localDate")] public string? LocalDate { get; set; }
    [JsonPropertyName("price")] public Amount? Price { get; set; }
    [JsonPropertyName("details")] public PriceDetails? Details { get; set; }
    [JsonPropertyName("resolution")] public string? Resolution { get; set; }
}

public sealed class Amount
{
    [JsonPropertyName("value")] public double Value { get; set; }
    [JsonPropertyName("vat")] public double Vat { get; set; }
    [JsonPropertyName("total")] public double Total { get; set; }
    [JsonPropertyName("unit")] public string? Unit { get; set; }
}

public sealed class PriceDetails
{
    [JsonPropertyName("electricity")] public Amount? Electricity { get; set; }
    [JsonPropertyName("surcharge")] public Amount? Surcharge { get; set; }
    [JsonPropertyName("transmission")] public Transmission? Transmission { get; set; }
    [JsonPropertyName("electricityTax")] public Amount? ElectricityTax { get; set; }
    [JsonPropertyName("distribution")] public Amount? Distribution { get; set; }
}

public sealed class Transmission
{
    [JsonPropertyName("systemTariff")] public Amount? SystemTariff { get; set; }
    [JsonPropertyName("netTariff")] public Amount? NetTariff { get; set; }
}

// ---------- /api/forecasts/latest ----------

public sealed class ForecastResponse
{
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("priceArea")] public string? PriceArea { get; set; }
    [JsonPropertyName("prices")] public List<ForecastPoint> Prices { get; set; } = new();
}

public sealed class ForecastPoint
{
    [JsonPropertyName("datetime")] public DateTimeOffset Time { get; set; }

    /// <summary>Spot price only, ex. VAT (kr/kWh) - comparable to details.electricity.value.</summary>
    [JsonPropertyName("price")] public double Price { get; set; }
}
