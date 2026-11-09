using System.Text.Json.Serialization;
using StromForbrug;
using Stromligning;

namespace StromForbrug.Web.Services;

/// <summary>The user's settings, kept in the browser's localStorage between visits.</summary>
public sealed class AppSetup
{
    // ---- Eloverblik ----
    public string? MeteringPointId { get; set; }

    /// <summary>The long-lived token the customer creates on eloverblik.dk ("refresh token").</summary>
    public string? RefreshToken { get; set; }

    // ---- stromligning.dk ----
    public string? SupplierId { get; set; }
    public string? CustomerGroupId { get; set; }
    // Companies have no id in the API, so they are remembered by name.
    public string? CompanyName { get; set; }
    public string? ProductId { get; set; }

    /// <summary>Show kroner incl. VAT (what was actually paid) rather than ex. VAT.</summary>
    public bool IncludeVat { get; set; } = true;

    /// <summary>The last view, so the page opens where the user left it.</summary>
    public Resolution Resolution { get; set; } = Resolution.Hour;

    [JsonIgnore]
    public bool IsComplete => !string.IsNullOrWhiteSpace(MeteringPointId) && !string.IsNullOrWhiteSpace(RefreshToken);

    [JsonIgnore]
    public bool HasPrices => !string.IsNullOrWhiteSpace(SupplierId);

    [JsonIgnore]
    public PriceQuery? PriceQuery => HasPrices ? new PriceQuery(SupplierId!, CustomerGroupId, ProductId) : null;

    public AppSetup Clone() => (AppSetup)MemberwiseClone();
}
