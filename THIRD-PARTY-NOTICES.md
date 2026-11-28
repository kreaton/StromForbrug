# Third-party notices

**StromForbrug** is © 2026 Per Hejndorf and is licensed under the [MIT License](LICENSE), except where stated below. This file lists the third-party components and assets it uses, each under its own licence. Licence texts and copyright notices are kept by the upstream projects at the *Source* links; where a licence requires its notice to travel with binaries, the upstream package already carries it.

_Last reviewed: 2026-11-17. Versions are the ones resolved in this repository at that date; Dependabot keeps them current, so check the project files for the exact version of a given release._

## Libraries included in the application

| Library | Version | Licence | Source | Modified |
|---|---|---|---|---|
| .NET runtime and Blazor WebAssembly (shipped in `_framework/`) | 10.0 | MIT | [dotnet/runtime](https://github.com/dotnet/runtime), [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) | No |
| Microsoft.AspNetCore.Components.WebAssembly | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebAssembly/10.0.14) | No |

<details>
<summary>Indirect (transitive) dependencies — 25 packages</summary>

These packages are pulled in by the libraries above and are shipped with the application. All are used unmodified.

| Library | Version | Licence | Source | Modified |
|---|---|---|---|---|
| Microsoft.AspNetCore.Authorization | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Authorization/10.0.14) | No |
| Microsoft.AspNetCore.Components | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Components/10.0.14) | No |
| Microsoft.AspNetCore.Components.Forms | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Forms/10.0.14) | No |
| Microsoft.AspNetCore.Components.Web | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Web/10.0.14) | No |
| Microsoft.AspNetCore.Metadata | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Metadata/10.0.14) | No |
| Microsoft.Extensions.Configuration | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration/10.0.14) | No |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Abstractions/10.0.14) | No |
| Microsoft.Extensions.Configuration.Binder | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/10.0.14) | No |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.FileExtensions/10.0.14) | No |
| Microsoft.Extensions.Configuration.Json | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Json/10.0.14) | No |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.14) | No |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.14) | No |
| Microsoft.Extensions.Diagnostics | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics/10.0.14) | No |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions/10.0.14) | No |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Abstractions/10.0.14) | No |
| Microsoft.Extensions.FileProviders.Physical | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Physical/10.0.14) | No |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.FileSystemGlobbing/10.0.14) | No |
| Microsoft.Extensions.Logging | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.14) | No |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.14) | No |
| Microsoft.Extensions.Options | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Options/10.0.14) | No |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions/10.0.14) | No |
| Microsoft.Extensions.Primitives | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/10.0.14) | No |
| Microsoft.Extensions.Validation | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Validation/10.0.14) | No |
| Microsoft.JSInterop | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.JSInterop/10.0.14) | No |
| Microsoft.JSInterop.WebAssembly | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.JSInterop.WebAssembly/10.0.14) | No |

</details>

## Build, test and tooling (not distributed)

Used to build, test or render promotional material only; not part of the shipped application.

| Library | Version | Licence | Source | Modified |
|---|---|---|---|---|
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 10.0.14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebAssembly.DevServer/10.0.14) | No |

## Fonts, data and online services

These are used at run time but are not bundled in this repository. Their own terms apply.

| Service | Used for | Terms | Source |
|---|---|---|---|
| Eloverblik (Energinet) | The user's own electricity metering data, fetched with the user's own token | Eloverblik / Energinet terms of use | [eloverblik.dk](https://eloverblik.dk) |
| Strømligning | Electricity spot and tariff prices | Strømligning API terms; data credited to Strømligning | [stromligning.dk](https://stromligning.dk) |

## Icons, images, audio and video

| Asset(s) | Origin | Licence |
|---|---|---|
| `StromForbrug.Web/wwwroot/favicon.ico`, `StromForbrug.Web/wwwroot/icons/` (8 files: .png) | AI-generated by the author with an image-generation tool, then edited/resized by the author | MIT (as part of this project) |
| `docs/forbrug-dag.jpg`, `docs/forbrug-time.png`, `docs/pris-dag.png` | Screenshot of this application, taken by the author | MIT (as part of this project) |
| `StromForbrug.Web/wwwroot/img/stromligning-logo.svg` | Strømligning logo | Trademark of Strømligning; shown only to credit Strømligning as the price-data source. **Not covered by the MIT License.** |

AI-generated images and audio were created for this project by the author. Purely AI-generated material may not be protected by copyright in some jurisdictions; to the extent it is, the licence in the table applies.

## Trademarks

Microsoft, Windows, .NET, Blazor and other Microsoft product names are trademarks of the Microsoft group of companies. All other product names, logos and brands mentioned in this repository are the property of their respective owners. They are used only to identify the services and tools this project works with, and their use does not imply endorsement.
