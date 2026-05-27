# Run every L2 integration test in tests/integration/M*.test.ps1.
# Exits non-zero on the first failure.
# Run: pwsh ./tests/integration/run-all.ps1

$ErrorActionPreference = 'Stop'
$failures = @()
$tests = Get-ChildItem -Path $PSScriptRoot -Filter 'M*.test.ps1' | Sort-Object Name

foreach ($t in $tests) {
    Write-Host ''
    Write-Host "─── running $($t.Name) ───" -ForegroundColor Cyan
    try {
        & $t.FullName
    } catch {
        $failures += "$($t.Name): $_"
        Write-Host "[FAIL] $($t.Name): $_" -ForegroundColor Red
    }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) test(s) FAILED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "all $($tests.Count) integration tests passed" -ForegroundColor Green
