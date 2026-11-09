using System.Globalization;
using Stromligning;

namespace StromForbrug.Web.Services;

/// <summary>
/// Grid companies and trading companies, fetched once per visit and shared by the pages.
/// The lists are sorted the Danish way and trimmed to what can be priced per hour.
/// </summary>
public sealed class CatalogService
{
    private static readonly StringComparer Da = StringComparer.Create(CultureInfo.GetCultureInfo("da-DK"), true);

    private readonly StromligningClient _client;
    private Task<IReadOnlyList<Supplier>>? _suppliers;
    private Task<IReadOnlyList<Company>>? _companies;

    public CatalogService(StromligningClient client) => _client = client;

    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync() => _suppliers ??= LoadSuppliersAsync();

    public Task<IReadOnlyList<Company>> GetCompaniesAsync() => _companies ??= LoadCompaniesAsync();

    private async Task<IReadOnlyList<Supplier>> LoadSuppliersAsync()
    {
        try
        {
            var list = await _client.GetSuppliersAsync(CancellationToken.None);
            return list.OrderBy(s => s.Name, Da).ToList();
        }
        catch
        {
            _suppliers = null; // let the next caller retry
            throw;
        }
    }

    private async Task<IReadOnlyList<Company>> LoadCompaniesAsync()
    {
        try
        {
            var list = await _client.GetCompaniesAsync(CancellationToken.None);
            return list.Where(c => c.SpotProducts.Any()).OrderBy(c => c.Name, Da).ToList();
        }
        catch
        {
            _companies = null;
            throw;
        }
    }
}
