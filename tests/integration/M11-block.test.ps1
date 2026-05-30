# L2 integration: create-rectangular-block should build a Front-Plane
# centered rectangle extruded along +Z, then save .sldprt. inspect-part is
# used to verify the bounding box matches the requested dimensions.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M11-block.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$bracket = Join-Path $tmpDir ("block_bracket_{0}.sldprt" -f (Get-Random))
$cube    = Join-Path $tmpDir ("block_cube_{0}.sldprt"    -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

# Tolerance for SW bbox round-trip in mm (Inspect returns SI meters × 1000).
$bboxTol = 0.01

try {
    # ── happy: bracket 100×50×20 mm ──────────────────────────────────────────
    $stdout = & $exe create-rectangular-block --length 100 --width 50 --height 20 --out $bracket --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-rectangular-block (bracket) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.status -ne 'ok')        { throw "json status: '$($result.status)'" }
    if ($result.path -ne $bracket)      { throw "json path mismatch: '$($result.path)' vs '$bracket'" }
    if (-not (Test-Path $bracket))      { throw "block .sldprt not created: $bracket" }
    $size = (Get-Item $bracket).Length
    if ($size -lt 1024)                 { throw "block .sldprt suspiciously small: $size bytes" }

    # Verify bounding box via inspect-part — sorted dims should be 20,50,100.
    $stdout = & $exe inspect-part --input $bracket --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-part on bracket failed: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    $dims = @($r.data.sizeMm.x, $r.data.sizeMm.y, $r.data.sizeMm.z) | Sort-Object
    if ([Math]::Abs($dims[0] - 20) -gt $bboxTol -or
        [Math]::Abs($dims[1] - 50) -gt $bboxTol -or
        [Math]::Abs($dims[2] - 100) -gt $bboxTol) {
        throw "bracket sizeMm sorted = $($dims -join ', '); expected 20,50,100"
    }
    if ($r.data.featureCount -ne 2)     { throw "bracket should have exactly 2 features (sketch + extrude), got $($r.data.featureCount)" }
    Write-Host ("[ok] bracket 100×50×20 -> {0} ({1:N0} bytes); inspect bbox sorted = {2:N2}×{3:N2}×{4:N2}" `
        -f $bracket, $size, $dims[0], $dims[1], $dims[2])

    # ── happy: cube 30×30×30 mm (sanity check for equal dims) ───────────────
    $stdout = & $exe create-rectangular-block --length 30 --width 30 --height 30 --out $cube --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-rectangular-block (cube) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    if (-not (Test-Path $cube))         { throw "cube .sldprt not created: $cube" }

    $stdout = & $exe inspect-part --input $cube --output json 2>$errFile
    $r = $stdout | ConvertFrom-Json
    foreach ($d in @($r.data.sizeMm.x, $r.data.sizeMm.y, $r.data.sizeMm.z)) {
        if ([Math]::Abs($d - 30) -gt $bboxTol) {
            throw "cube dim $d ≠ 30 (tol $bboxTol)"
        }
    }
    Write-Host ("[ok] cube 30³ -> {0} ({1:N0} bytes); inspect confirms all 3 dims ≈ 30" -f $cube, (Get-Item $cube).Length)

    # ── validation: negative length (spec layer) ────────────────────────────
    $bad = Join-Path $tmpDir "should_not_exist.sldprt"
    & $exe create-rectangular-block --length -10 --width 50 --height 20 --out $bad 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for negative length" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'length')     { throw "error message missing 'length': $errMsg" }
    Write-Host "[ok] validation rejects negative length"

    # ── validation: oversize width (spec layer) ─────────────────────────────
    & $exe create-rectangular-block --length 100 --width 50000 --height 20 --out $bad 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for oversize width" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'range')      { throw "error message missing 'range': $errMsg" }
    Write-Host "[ok] validation rejects oversize width"

    Write-Host '[ok] M11-block all checks passed'
} finally {
    foreach ($f in @($bracket, $cube, $errFile, (Join-Path $tmpDir 'should_not_exist.sldprt'))) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
