# L2 integration: create-cylinder should produce a real .sldprt on disk.
# Requires SolidWorks to be installed and runnable on this machine.
# Run: pwsh ./tests/integration/M2-cylinder.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures, which trips Stop-mode. We do
# explicit $LASTEXITCODE + Test-Path checks instead.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$happyOut = Join-Path $tmpDir ("cyl_{0:yyyyMMdd_HHmmss}_{1}.sldprt" -f (Get-Date), (Get-Random))
$errFile  = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy path ──────────────────────────────────────────────────────────
    $stdout = & $exe create-cylinder --diameter 30 --length 50 --out $happyOut --output json 2>$errFile
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "create-cylinder exited $exit. stderr: $errTxt; stdout: $stdout"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.status -ne 'ok')      { throw "json status: '$($result.status)'; stdout was: $stdout" }
    if ($result.path -ne $happyOut)   { throw "json path mismatch: '$($result.path)' vs '$happyOut'" }
    if (-not (Test-Path $happyOut))   { throw "file not created: $happyOut" }
    $size = (Get-Item $happyOut).Length
    if ($size -lt 1024)               { throw ".sldprt suspiciously small: $size bytes" }
    Write-Host ("[ok] happy path D30 L50 -> {0} ({1:N0} bytes)" -f $happyOut, $size)

    # ── validation: negative diameter ───────────────────────────────────────
    $badOut = Join-Path $tmpDir 'bad_dim.sldprt'
    & $exe create-cylinder --diameter -5 --length 50 --out $badOut 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for negative diameter" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'diameter')   { throw "error message did not mention diameter: $errMsg" }
    if (Test-Path $badOut)              { throw "bad-spec file should not have been created" }
    Write-Host "[ok] validation rejects negative diameter (exit=$LASTEXITCODE)"

    # ── validation: wrong extension ─────────────────────────────────────────
    $badExt = Join-Path $tmpDir 'cyl.step'
    & $exe create-cylinder --diameter 30 --length 50 --out $badExt 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for wrong extension" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch '\.sldprt')   { throw "error message did not mention .sldprt: $errMsg" }
    Write-Host "[ok] validation rejects non-.sldprt extension"

    Write-Host '[ok] M2-cylinder all checks passed'
} finally {
    if (Test-Path $happyOut) { Remove-Item $happyOut -Force -ErrorAction SilentlyContinue }
    if (Test-Path $errFile)  { Remove-Item $errFile  -Force -ErrorAction SilentlyContinue }
}
