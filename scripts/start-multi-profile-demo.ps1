param(
    [switch]$IncludeDirector2,
    [ValidateSet("Trace", "Debug", "Information", "Warning", "Error")]
    [string]$LogLevel = "Debug",
    [switch]$TelemetryFastDebug
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot

try {
    Write-Host "Building EzCompany.App..."
    dotnet build src/EzCompany.App/EzCompany.App.csproj -c Debug
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }

    $exePath = Join-Path $RepoRoot "src\EzCompany.App\bin\Debug\net8.0-windows\EzCompany.App.exe"
    if (-not (Test-Path $exePath)) {
        throw "Could not find built executable at $exePath"
    }

    $profiles = @("director1", "accountant1", "legal1")
    if ($IncludeDirector2) {
        $profiles += "director2"
    }

    $env:EZCOMPANY_LOG_LEVEL = $LogLevel
    if ($TelemetryFastDebug -or -not $PSBoundParameters.ContainsKey("TelemetryFastDebug")) {
        $env:EZCOMPANY_TELEMETRY_DEBUG = "1"
    }

    Write-Host ""
    Write-Host "Launching profiles: $($profiles -join ', ')"
    Write-Host "EZCOMPANY_LOG_LEVEL=$($env:EZCOMPANY_LOG_LEVEL), EZCOMPANY_TELEMETRY_DEBUG=$($env:EZCOMPANY_TELEMETRY_DEBUG)"
    Write-Host ""

    foreach ($p in $profiles) {
        $logDir = Join-Path $env:LOCALAPPDATA "EzCompany\Profiles\$p\Logs"
        Write-Host "  -> [$p] Logs: $logDir"
        Start-Process -FilePath $exePath -ArgumentList "--profile=$p" -WorkingDirectory $RepoRoot
        Start-Sleep -Milliseconds 400
    }

    Write-Host ""
    Write-Host "Tip: Tail live logs in PowerShell with:"
    Write-Host "  Get-Content `"$env:LOCALAPPDATA\EzCompany\Profiles\director1\Logs\ezcompany-$(Get-Date -Format 'yyyyMMdd').log`" -Wait"
}
finally {
    Pop-Location
}
