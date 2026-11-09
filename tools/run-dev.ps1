<#
.SYNOPSIS
    Runs StromForbrug locally: the Eloverblik relay (PHP) and the web app (Blazor dev server).

.DESCRIPTION
    Browsers may not read Eloverblik's answers directly, so the web app goes through
    wwwroot/api/eloverblik.php. The Blazor dev server cannot run PHP, so this script starts PHP's
    built-in server for the relay on port 5290 (wwwroot/appsettings.Development.json points the
    app there) and then the app on port 5286. Ctrl+C stops both.

    PHP is found on PATH (e.g. "winget install PHP.PHP.8.5"). If its php.ini does not enable
    curl, curl and openssl are switched on for this run only.

.PARAMETER RelayPort
    Port for the PHP relay. Must match appsettings.Development.json.

.PARAMETER NoBrowser
    Do not open the app in the default browser.
#>
param(
    [int]$RelayPort = 5290,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$web = Join-Path $root 'StromForbrug.Web'
$wwwroot = Join-Path $web 'wwwroot'

$php = (Get-Command php -ErrorAction SilentlyContinue).Source
if (-not $php) { throw 'PHP was not found on PATH. Install it, e.g. "winget install PHP.PHP.8.5".' }

foreach ($port in $RelayPort, 5286) {
    $holder = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($holder) {
        $name = (Get-Process -Id $holder.OwningProcess -ErrorAction SilentlyContinue).ProcessName
        throw "Port $port is already in use by $name (PID $($holder.OwningProcess)). Stop it first."
    }
}

$phpArgs = @('-S', "localhost:$RelayPort", '-t', $wwwroot)
if (-not ((& $php -m) -contains 'curl')) {
    # No php.ini enabling curl: switch it (and openssl, for https) on for this run only.
    $ext = Join-Path (Split-Path $php) 'ext'
    $phpArgs = @('-d', "extension_dir=$ext", '-d', 'extension=curl', '-d', 'extension=openssl') + $phpArgs
}

Write-Host "Relay: http://localhost:$RelayPort/api/eloverblik.php  (PHP $(& $php -r 'echo PHP_VERSION;'))" -ForegroundColor Cyan
$relay = Start-Process -FilePath $php -ArgumentList $phpArgs -NoNewWindow -PassThru

try {
    Start-Sleep -Milliseconds 500
    if ($relay.HasExited) { throw "PHP stopped at once (exit code $($relay.ExitCode))." }

    if (-not $NoBrowser) {
        # Open the app once the dev server answers.
        Start-Job -ArgumentList 'http://localhost:5286/' -ScriptBlock {
            param($url)
            for ($i = 0; $i -lt 120; $i++) {
                try { Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2 | Out-Null; Start-Process $url; return } catch { Start-Sleep 1 }
            }
        } | Out-Null
    }

    Write-Host 'App:   http://localhost:5286/  (Ctrl+C stops both)' -ForegroundColor Cyan
    & dotnet run --project $web --launch-profile http
}
finally {
    if (-not $relay.HasExited) { Stop-Process -Id $relay.Id -Force }
    Get-Job | Remove-Job -Force -ErrorAction SilentlyContinue
    Write-Host 'Relay stopped.' -ForegroundColor Cyan
}
