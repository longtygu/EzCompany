param(
    [string]$Version = "0.4.0",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot

try {
    $cleanVer = $Version.TrimStart('v', 'V')
    $numericBase = ($cleanVer -split '[-+]')[0]
    $asmVersion = "$numericBase.0"

    Write-Host "=== Ez Company Velopack Setup Builder ==="
    Write-Host "Version:         $cleanVer"
    Write-Host "AssemblyVersion: $asmVersion"
    Write-Host ""

    if (-not $SkipTests) {
        Write-Host "[1/5] Running Release build & xUnit test suite..."
        dotnet restore EzCompany.sln
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

        dotnet build EzCompany.sln -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

        dotnet test EzCompany.sln -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }
    }
    else {
        Write-Host "[1/5] Skipping tests (-SkipTests specified)."
    }

    $publishDir = Join-Path $RepoRoot "publish\win-x64"
    $releasesDir = Join-Path $RepoRoot "Releases"

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
    New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null

    Write-Host "[2/5] Publishing self-contained win-x64 application..."
    dotnet publish src/EzCompany.App/EzCompany.App.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:Version=$cleanVer `
        -p:AssemblyVersion=$asmVersion `
        -p:FileVersion=$asmVersion `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    Write-Host "[3/5] Resolving Velopack CLI (vpk 1.2.161)..."
    $vpkCmd = Get-Command vpk -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
    if (-not $vpkCmd) {
        $toolsDir = Join-Path $RepoRoot ".tools"
        $localVpk = Join-Path $toolsDir "vpk.exe"
        if (-not (Test-Path $localVpk)) {
            New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
            dotnet tool install --tool-path $toolsDir vpk --version 1.2.161
            if ($LASTEXITCODE -ne 0) { throw "Failed to install vpk CLI." }
        }
        $vpkCmd = $localVpk
    }
    Write-Host "Using vpk: $vpkCmd"

    Write-Host "[4/5] Packing Velopack installer, packages & releases.win.json..."
    & $vpkCmd pack `
        --packId EzCompany `
        --packVersion $cleanVer `
        --packDir $publishDir `
        --mainExe EzCompany.App.exe `
        --outputDir $releasesDir `
        --packTitle "Ez Company" `
        --icon (Join-Path $RepoRoot "src\EzCompany.App\Assets\EzCompany.ico") `
        --runtime win-x64 `
        --channel win
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE." }

    if (-not (Get-ChildItem -Path $releasesDir -Filter "*Portable*.zip")) {
        $portableZip = Join-Path $releasesDir "EzCompany-$cleanVer-win-Portable.zip"
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portableZip -Force
    }

    Write-Host "[5/5] Generating Releases/SHA256SUMS.txt..."
    $shaFile = Join-Path $releasesDir "SHA256SUMS.txt"
    if (Test-Path $shaFile) { Remove-Item $shaFile -Force }

    $sumLines = Get-ChildItem -Path $releasesDir -File | Sort-Object Name | ForEach-Object {
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
    $sumLines | Out-File -FilePath $shaFile -Encoding utf8

    Write-Host ""
    Write-Host "=== Release Artifacts Created ==="
    Get-ChildItem -Path $releasesDir -File | Sort-Object Name | ForEach-Object {
        $mb = [Math]::Round($_.Length / 1MB, 2)
        Write-Host ("  {0,-42} {1,8:N2} MB" -f $_.Name, $mb)
    }
    Write-Host ""
    Write-Host "SHA256SUMS.txt:"
    Get-Content $shaFile | ForEach-Object { Write-Host "  $_" }
}
finally {
    Pop-Location
}
