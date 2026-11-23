param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot

try {
    $ResultsDir = Join-Path $RepoRoot "TestResults"
    New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

    $Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $VerifyLog = Join-Path $ResultsDir "local-verify-$Timestamp.log"
    $TrxPath = "test-results-$Timestamp.trx"

    function Write-StepLog([string]$Message) {
        $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message"
        Write-Host $line
        Add-Content -Path $VerifyLog -Value $line -Encoding utf8
    }

    Write-StepLog "=== Ez Company Local Build & Test Verification ($Configuration) ==="
    Write-StepLog "dotnet SDK: $(dotnet --version)"
    Write-StepLog "Repo root: $RepoRoot"

    Write-StepLog "Step 1/3: dotnet restore EzCompany.sln"
    dotnet restore EzCompany.sln 2>&1 | Tee-Object -FilePath $VerifyLog -Append
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    Write-StepLog "Step 2/3: dotnet build EzCompany.sln -c $Configuration --no-restore"
    dotnet build EzCompany.sln -c $Configuration --no-restore 2>&1 | Tee-Object -FilePath $VerifyLog -Append
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    Write-StepLog "Step 3/3: dotnet test EzCompany.sln -c $Configuration --no-build --verbosity normal"
    dotnet test EzCompany.sln -c $Configuration --no-build --verbosity normal --logger "trx;LogFileName=$TrxPath" --results-directory $ResultsDir 2>&1 | Tee-Object -FilePath $VerifyLog -Append
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

    Write-StepLog "=== VERIFICATION PASSED ==="
    Write-StepLog "Full text log: $VerifyLog"
    Write-StepLog "TRX report:    $(Join-Path $ResultsDir $TrxPath)"
    Write-StepLog "Runtime profile logs are written under: $env:LOCALAPPDATA\EzCompany\Profiles\<profile>\Logs\"
}
finally {
    Pop-Location
}
