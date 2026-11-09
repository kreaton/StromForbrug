# BuildRelease.ps1
# Builds the next version of the Windows app, packs it with Velopack and publishes it as a
# GitHub release (Setup.exe, portable zip and update packages).
# GitHub Actions runs this on pushes to main that touch the Windows app (.github/workflows/release-windows.yml).
#   .\tools\BuildRelease.ps1             build, pack and upload
#   .\tools\BuildRelease.ps1 -LocalOnly  build and pack into .\Releases only
#
# Requires the vpk tool, matching the Velopack package version in StromForbrug.Windows.csproj:
#   dotnet tool install -g vpk --version 1.2.161
# and, unless -LocalOnly, a GitHub token: $env:GH_TOKEN in CI, otherwise the GitHub CLI (gh auth login).
param([switch]$LocalOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -Path $root

$AppName = "StromForbrug"
$RepoUrl = "https://github.com/kreaton/StromForbrug"
$Project = ".\StromForbrug.Windows\StromForbrug.Windows.csproj"
$PublishDir = ".\publish-windows"
$ReleasesDir = ".\Releases"

if (-not $LocalOnly) {
    # Fetch the latest published release so the version continues from it and vpk can build a delta.
    $token = if ($env:GH_TOKEN) { $env:GH_TOKEN } else { gh auth token }
    if (-not $token) { throw "No GitHub token (set GH_TOKEN or run: gh auth login)" }
    vpk download github --repoUrl $RepoUrl --token $token -o $ReleasesDir
    if ($LASTEXITCODE -ne 0) { throw "vpk download failed" }
}

# Major.Minor comes from the csproj; the patch is one higher than the last release with that
# Major.Minor, or 0 for the first one. It is read from the full package's file name because
# vpk download only fetches the package: a clean Releases folder has no releases.win.json.
$majorMinor = (([xml](Get-Content -Path $Project -Raw)).Project.PropertyGroup | Where-Object MajorMinor).MajorMinor
$buildNumber = 0
$last = Get-ChildItem -Path $ReleasesDir -Filter "$AppName-*-full.nupkg" -ErrorAction SilentlyContinue
    | ForEach-Object { [version]($_.Name -replace "^$AppName-|-full\.nupkg$") }
    | Where-Object { "$($_.Major).$($_.Minor)" -eq $majorMinor }
    | Sort-Object -Descending
    | Select-Object -First 1
if ($last) { $buildNumber = $last.Build + 1 }
$newVersion = "$majorMinor.$buildNumber"

Write-Host "Publishing $AppName version $newVersion (framework-dependent, win-x64)"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $Project -c Release -r win-x64 --self-contained false -p:BuildNumber=$buildNumber -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Packing $AppName version $newVersion with Velopack"
# --framework makes Setup.exe install the .NET 10 Desktop Runtime if it is missing.
vpk pack -u $AppName -v $newVersion -p $PublishDir -e "$AppName.exe" -o $ReleasesDir `
    --icon .\StromForbrug.Windows\app.ico --runtime win-x64 --framework net10.0-x64-desktop
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

if (-not $LocalOnly) {
    Write-Host "Uploading $AppName version $newVersion to GitHub"
    # In CI, tag the commit that was built rather than whatever the branch points at by now.
    $commitish = if ($env:GITHUB_SHA) { @("--targetCommitish", $env:GITHUB_SHA) } else { @() }
    vpk upload github --repoUrl $RepoUrl --token $token -o $ReleasesDir --publish --tag "v$newVersion" `
        --releaseName "$AppName $newVersion (Windows)" @commitish
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }
}

Write-Host "Built $AppName version $newVersion"
