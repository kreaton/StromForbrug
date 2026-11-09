using System.Globalization;
using Eloverblik;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StromForbrug;
using StromForbrug.Web;
using StromForbrug.Web.Services;
using Stromligning;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var danish = CultureInfo.GetCultureInfo("da-DK");
CultureInfo.DefaultThreadCurrentCulture = danish;
CultureInfo.DefaultThreadCurrentUICulture = danish;

// Strømligning allows browsers to read its answers ("Access-Control-Allow-Origin: *"), so it is
// called directly. Eloverblik's successful answers lack that header, so the browser would throw
// them away: Eloverblik goes through the relay wwwroot/api/eloverblik.php on the app's own site.
// "EloverblikRelay" (wwwroot/appsettings.Development.json) points elsewhere during development,
// where the dev server does not run PHP; see tools/run-dev.ps1.
builder.Services.AddSingleton(_ => new StromligningClient(
    new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, BuildInfo.UserAgent));
var relay = new Uri(new Uri(builder.HostEnvironment.BaseAddress),
    builder.Configuration["EloverblikRelay"] ?? "api/eloverblik.php").ToString();
builder.Services.AddSingleton(_ => new EloverblikClient(
    new HttpClient { Timeout = TimeSpan.FromSeconds(100) }, BuildInfo.UserAgent, relay));
builder.Services.AddSingleton<ConsumptionService>();
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddSingleton<SetupStore>();

await builder.Build().RunAsync();
