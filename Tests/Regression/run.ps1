# Run Phase 13G-4 regression checks from any working directory.
# Requires .NET 8 SDK and Python 3. No game database or published content is modified.
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Push-Location $root
try {
    $failed = 0
    function Invoke-Check {
        param([string]$Title, [string]$Exe, [string[]]$ArgsList)
        Write-Host "`n=== $Title ===" -ForegroundColor Cyan
        & $Exe @ArgsList
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED: $Title" -ForegroundColor Red
            $script:failed++
        }
    }

    Invoke-Check "Production C# rules and guards" "dotnet" @("run", "--project", "Tests/Regression/Regression.csproj", "--configuration", "Release")
    Invoke-Check "Memory/completion SQL contracts" "python" @("Tests/Regression/sqlite_contracts.py")
    Invoke-Check "Studio/Godot shared rule parity" "python" @("Tests/Regression/studio_parity.py")

    # Keep exercising earlier reliability suites if the corresponding patches were applied.
    if (Test-Path "Tests/ChoiceResolutionGuardTests/ChoiceResolutionGuardTests.csproj") {
        Invoke-Check "13G-2B duplicate-choice suite" "dotnet" @("run", "--project", "Tests/ChoiceResolutionGuardTests/ChoiceResolutionGuardTests.csproj")
    }
    if (Test-Path "Tests/EncounterCompletionGuardTests/EncounterCompletionGuardTests.csproj") {
        Invoke-Check "13G-3 encounter-completion suite" "dotnet" @("run", "--project", "Tests/EncounterCompletionGuardTests/EncounterCompletionGuardTests.csproj")
    }
    if (Test-Path "Tests/EncounterCompletionSqliteTests/run.py") {
        Invoke-Check "13G-3 completion SQLite suite" "python" @("Tests/EncounterCompletionSqliteTests/run.py")
    }

    if ($failed -gt 0) {
        Write-Host "`n$failed regression group(s) failed." -ForegroundColor Red
        exit 1
    }
    Write-Host "`nAll available regression groups passed." -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
