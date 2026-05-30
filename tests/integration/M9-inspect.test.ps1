# L2 integration: inspect-part should return structured metadata for an
# existing part. Verifies title / featureCount / bodyCount / face+edge counts
# / sizeMm + boundingBoxMm shape via JSON output.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M9-inspect.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cyl     = Join-Path $tmpDir ("inspect_cyl_{0}.sldprt"    -f (Get-Random))
$flange  = Join-Path $tmpDir ("inspect_flange_{0}.sldprt" -f (Get-Random))
$missing = Join-Path $tmpDir ("no_such_{0}.sldprt"        -f ([Guid]::NewGuid()))
$errFile = Join-Path $tmpDir 'stderr.txt'

# Tolerance for SW bounding-box round-trip in mm (SW returns SI meters; we
# convert ×1000 and compare back to the input nominal dimension).
$bboxTol = 0.01

function New-SourceCylinder([string]$path, [double]$d, [double]$len) {
    & $exe create-cylinder --diameter $d --length $len --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "setup create-cylinder failed (exit $LASTEXITCODE). stderr: $errTxt"
    }
    if (-not (Test-Path $path)) { throw "setup cylinder not created: $path" }
}

function New-SourceFlange([string]$path, [double]$od, [double]$thick) {
    & $exe create-flange --outer $od --thickness $thick --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "setup create-flange failed (exit $LASTEXITCODE). stderr: $errTxt"
    }
    if (-not (Test-Path $path)) { throw "setup flange not created: $path" }
}

try {
    # ── happy: inspect a D40 × L30 cylinder ────────────────────────────────
    New-SourceCylinder $cyl 40 30
    $stdout = & $exe inspect-part --input $cyl --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "inspect-part (cyl) exited $LASTEXITCODE. stderr: $errTxt; stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')              { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($null -eq $r.data)               { throw "data block missing on inspect_part" }
    if ($r.data.bodyCount -ne 1)         { throw "expected 1 body, got $($r.data.bodyCount)" }
    # create_cylinder produces exactly 1 sketch + 1 boss-extrude = 2 user features.
    # If this changes, either the cylinder pipeline grew or the *Folder filter
    # missed a new SW 2026 boot container; investigate before relaxing.
    if ($r.data.featureCount -ne 2)      { throw "expected exactly 2 user features (sketch + extrude), got $($r.data.featureCount): $($r.data.features | ConvertTo-Json -Compress)" }
    if ($r.data.totalFaceCount -lt 3)    { throw "cylinder should have ≥3 faces, got $($r.data.totalFaceCount)" }
    if ($r.data.totalEdgeCount -lt 2)    { throw "cylinder should have ≥2 edges, got $($r.data.totalEdgeCount)" }
    # sizeMm: cylinder D40 L30 extruded from Front Plane → X≈40, Y≈40, Z≈30 (or perm.)
    $dims = @($r.data.sizeMm.x, $r.data.sizeMm.y, $r.data.sizeMm.z) | Sort-Object
    if ([Math]::Abs($dims[0] - 30) -gt $bboxTol -or
        [Math]::Abs($dims[1] - 40) -gt $bboxTol -or
        [Math]::Abs($dims[2] - 40) -gt $bboxTol) {
        throw "cylinder sizeMm sorted = $($dims -join ', '); expected ~30,40,40"
    }
    Write-Host ("[ok] inspect D40 L30 cyl: {0} bodies, {1} features, {2} faces, {3} edges; size {4:N2}×{5:N2}×{6:N2}" `
        -f $r.data.bodyCount, $r.data.featureCount, $r.data.totalFaceCount, $r.data.totalEdgeCount,
           $r.data.sizeMm.x, $r.data.sizeMm.y, $r.data.sizeMm.z)

    # ── happy: inspect a solid disk flange D80 × t10 ────────────────────────
    New-SourceFlange $flange 80 10
    $stdout = & $exe inspect-part --input $flange --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "inspect-part (flange) exited $LASTEXITCODE. stderr: $errTxt"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.data.bodyCount -ne 1)         { throw "flange bodyCount: got $($r.data.bodyCount)" }
    $dims = @($r.data.sizeMm.x, $r.data.sizeMm.y, $r.data.sizeMm.z) | Sort-Object
    if ([Math]::Abs($dims[0] - 10) -gt $bboxTol -or
        [Math]::Abs($dims[1] - 80) -gt $bboxTol -or
        [Math]::Abs($dims[2] - 80) -gt $bboxTol) {
        throw "flange sizeMm sorted = $($dims -join ', '); expected ~10,80,80"
    }
    Write-Host ("[ok] inspect D80 t10 flange: bbox sorted = {0:N2}×{1:N2}×{2:N2}" -f $dims[0], $dims[1], $dims[2])

    # ── validation: nonexistent input (spec layer) ──────────────────────────
    & $exe inspect-part --input $missing 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')       { throw "error message missing 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    # ── validation: wrong extension (spec layer) ────────────────────────────
    $wrongExt = Join-Path $tmpDir 'inspect_wrong.step'
    & $exe inspect-part --input $wrongExt 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for non-.sldprt input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch '\.sldprt')    { throw "error message missing '.sldprt': $errMsg" }
    Write-Host "[ok] validation rejects non-.sldprt input"

    Write-Host '[ok] M9-inspect all checks passed'
} finally {
    foreach ($f in @($cyl, $flange, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
