# L2 integration: add-fillet should round every edge of an existing part.
# It is the first "edit an existing part" tool, so each case first creates a
# source cylinder with create-cylinder, then fillets it. A zero exit code
# already proves FeatureFillet3 did not return null (the tool throws on null).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M4-fillet.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cylA      = Join-Path $tmpDir ("fillet_src_a_{0}.sldprt"  -f (Get-Random))
$cylB      = Join-Path $tmpDir ("fillet_src_b_{0}.sldprt"  -f (Get-Random))
$filletedA = Join-Path $tmpDir ("filleted_a_{0}.sldprt"    -f (Get-Random))
$missing   = Join-Path $tmpDir ("no_such_{0}.sldprt"       -f ([Guid]::NewGuid()))
$errFile   = Join-Path $tmpDir 'stderr.txt'

function New-SourceCylinder([string]$path, [double]$d, [double]$len) {
    & $exe create-cylinder --diameter $d --length $len --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "setup create-cylinder failed (exit $LASTEXITCODE). stderr: $errTxt"
    }
    if (-not (Test-Path $path)) { throw "setup cylinder not created: $path" }
}

try {
    # ── happy path: save a filleted copy, leave the source intact ───────────
    New-SourceCylinder $cylA 30 50
    $stdout = & $exe add-fillet --input $cylA --radius 2 --out $filletedA --output json 2>$errFile
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "add-fillet (copy) exited $exit. stderr: $errTxt; stdout: $stdout"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.status -ne 'ok')      { throw "json status: '$($result.status)'; stdout: $stdout" }
    if ($result.path -ne $filletedA)  { throw "json path mismatch: '$($result.path)' vs '$filletedA'" }
    if (-not (Test-Path $filletedA))  { throw "filleted copy not created: $filletedA" }
    if (-not (Test-Path $cylA))       { throw "source should be preserved when saving a copy: $cylA" }
    $size = (Get-Item $filletedA).Length
    if ($size -lt 1024)               { throw "filleted .sldprt suspiciously small: $size bytes" }
    Write-Host ("[ok] fillet R2 -> copy {0} ({1:N0} bytes), source preserved" -f $filletedA, $size)

    # ── happy path: in-place overwrite (no --out) ───────────────────────────
    New-SourceCylinder $cylB 40 60
    $stdout = & $exe add-fillet --input $cylB --radius 3 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "add-fillet (in-place) exited $LASTEXITCODE. stderr: $errTxt"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.path -ne $cylB)       { throw "in-place path should equal input: '$($result.path)' vs '$cylB'" }
    if (-not (Test-Path $cylB))       { throw "in-place file missing after fillet: $cylB" }
    Write-Host ("[ok] fillet R3 in-place -> {0} ({1:N0} bytes)" -f $cylB, (Get-Item $cylB).Length)

    # ── validation: nonexistent input (spec layer, never touches SW) ────────
    & $exe add-fillet --input $missing --radius 2 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')    { throw "error message missing 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    # ── validation: non-positive radius (spec layer) ────────────────────────
    & $exe add-fillet --input $cylA --radius -1 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for negative radius" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'radius')   { throw "error message missing 'radius': $errMsg" }
    Write-Host "[ok] validation rejects negative radius"

    # ── SW layer: radius larger than the geometry → FeatureFillet3 null ─────
    #   R30 on a D30 (r=15) cylinder is geometrically impossible; the tool must
    #   surface FeatureFillet3's null return as a non-zero exit, not a silent ok.
    & $exe add-fillet --input $cylA --radius 30 --out (Join-Path $tmpDir 'should_not_exist.sldprt') 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for impossibly large radius" }
    Write-Host "[ok] oversized radius surfaces SW failure as non-zero exit"

    Write-Host '[ok] M4-fillet all checks passed'
} finally {
    foreach ($f in @($cylA, $cylB, $filletedA, $errFile, (Join-Path $tmpDir 'should_not_exist.sldprt'))) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
