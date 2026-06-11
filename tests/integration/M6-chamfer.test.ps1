# L2 integration: add-chamfer should chamfer every edge of an existing part.
# Mirrors M4-fillet (same open → select edges → feature → save pipeline) but
# verifies InsertFeatureChamfer instead of FeatureFillet3.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M6-chamfer.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cylA       = Join-Path $tmpDir ("chamfer_src_a_{0}.sldprt"  -f (Get-Random))
$cylB       = Join-Path $tmpDir ("chamfer_src_b_{0}.sldprt"  -f (Get-Random))
$chamferedA = Join-Path $tmpDir ("chamfered_a_{0}.sldprt"    -f (Get-Random))
$missing    = Join-Path $tmpDir ("no_such_{0}.sldprt"        -f ([Guid]::NewGuid()))
$errFile    = Join-Path $tmpDir 'stderr.txt'

function New-SourceCylinder([string]$path, [double]$d, [double]$len) {
    & $exe create-cylinder --diameter $d --length $len --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "setup create-cylinder failed (exit $LASTEXITCODE). stderr: $errTxt"
    }
    if (-not (Test-Path $path)) { throw "setup cylinder not created: $path" }
}

try {
    # ── happy path: save a chamfered copy, leave the source intact ──────────
    New-SourceCylinder $cylA 30 50
    $stdout = & $exe add-chamfer --input $cylA --distance 2 --out $chamferedA --output json 2>$errFile
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "add-chamfer (copy) exited $exit. stderr: $errTxt; stdout: $stdout"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.status -ne 'ok')        { throw "json status: '$($result.status)'; stdout: $stdout" }
    if ($result.path -ne $chamferedA)   { throw "json path mismatch: '$($result.path)' vs '$chamferedA'" }
    if (-not (Test-Path $chamferedA))   { throw "chamfered copy not created: $chamferedA" }
    if (-not (Test-Path $cylA))         { throw "source should be preserved when saving a copy: $cylA" }
    $size = (Get-Item $chamferedA).Length
    if ($size -lt 1024)                 { throw "chamfered .sldprt suspiciously small: $size bytes" }
    # M52 regression guard: the chamfer must CHANGE GEOMETRY. With the original
    # swChamferEqualDistance(16)+Angle=0 call the feature was degenerate (in
    # the tree, zero geometry) and this tool was a silent no-op since M6 —
    # a chamfered cylinder must have 5 faces (3 + 2 conical rings), not 3.
    $topo = (& $exe inspect-part --input $chamferedA --output json 2>$errFile) | ConvertFrom-Json
    if ($topo.data.totalFaceCount -ne 5) {
        throw "chamfer did not change geometry: expected 5 faces, got $($topo.data.totalFaceCount)"
    }
    Write-Host ("[ok] chamfer D2 -> copy {0} ({1:N0} bytes, 5 faces), source preserved" -f $chamferedA, $size)

    # ── happy path: in-place overwrite (no --out) ───────────────────────────
    New-SourceCylinder $cylB 40 60
    $stdout = & $exe add-chamfer --input $cylB --distance 3 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "add-chamfer (in-place) exited $LASTEXITCODE. stderr: $errTxt"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.path -ne $cylB)         { throw "in-place path should equal input: '$($result.path)' vs '$cylB'" }
    if (-not (Test-Path $cylB))         { throw "in-place file missing after chamfer: $cylB" }
    Write-Host ("[ok] chamfer D3 in-place -> {0} ({1:N0} bytes)" -f $cylB, (Get-Item $cylB).Length)

    # ── validation: nonexistent input (spec layer, never touches SW) ────────
    & $exe add-chamfer --input $missing --distance 2 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')      { throw "error message missing 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    # ── validation: non-positive distance (spec layer) ──────────────────────
    & $exe add-chamfer --input $cylA --distance -1 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for negative distance" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'distance')   { throw "error message missing 'distance': $errMsg" }
    Write-Host "[ok] validation rejects negative distance"

    # ── (no SW-null-return case) ────────────────────────────────────────────
    #   Unlike fillet (M4-fillet case 5 uses R30 on a D30 cylinder to drive
    #   FeatureFillet3 → null), SW InsertFeatureChamfer is extremely tolerant:
    #   distances up to 1000 mm on a D30 L50 cylinder still produce a (degenerate)
    #   chamfer feature, and even a 2nd chamfer over an already-chamfered file
    #   silently succeeds by chamfering the newly-introduced edges. So the
    #   "InsertFeatureChamfer returned null" branch in ChamferTool stays as
    #   defensive code, but L2 can't reach it from a fresh cylinder.

    Write-Host '[ok] M6-chamfer all checks passed'
} finally {
    foreach ($f in @($cylA, $cylB, $chamferedA, $errFile, (Join-Path $tmpDir 'should_not_exist.sldprt'))) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
