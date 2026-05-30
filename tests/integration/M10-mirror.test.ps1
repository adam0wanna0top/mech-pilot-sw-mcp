# L2 integration: mirror-feature should reflect a feature across Front/Top/
# Right. Builds source parts with create_cylinder + add_axial_hole first so
# there's an asymmetric cut feature to mirror. Requires SolidWorks installed.
# Run: pwsh ./tests/integration/M10-mirror.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$srcA    = Join-Path $tmpDir ("mirror_src_a_{0}.sldprt"  -f (Get-Random))
$srcB    = Join-Path $tmpDir ("mirror_src_b_{0}.sldprt"  -f (Get-Random))
$copyA   = Join-Path $tmpDir ("mirror_copy_a_{0}.sldprt" -f (Get-Random))
$missing = Join-Path $tmpDir ("no_such_{0}.sldprt"       -f ([Guid]::NewGuid()))
$errFile = Join-Path $tmpDir 'stderr.txt'

function New-CylinderWithOffsetHole([string]$path, [double]$d, [double]$len, [double]$holeDia, [double]$offX, [double]$offY) {
    & $exe create-cylinder --diameter $d --length $len --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "setup create-cylinder failed: $(Get-Content $errFile -Raw)"
    }
    & $exe add-axial-hole --input $path --diameter $holeDia --position-x $offX --position-y $offY --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "setup add-axial-hole failed: $(Get-Content $errFile -Raw)"
    }
    if (-not (Test-Path $path)) { throw "setup part not created: $path" }
}

try {
    # ── happy: hole offset in +X → mirror across Right plane (YZ) → -X copy ──
    #   Geometry note: create_cylinder sketches on Front Plane (XY) and
    #   extrudes along +Z. add_axial_hole on the +Z end face places the hole
    #   in world (x, y, z=L). To get a mirror-able hole, offset in X and
    #   mirror across Right Plane (X=0) — this lands the copy at (-x, y, z=L),
    #   still inside the disk for |x| < radius. Mirroring across Front
    #   (Z=0) would send the copy to z=-L, outside the part — SW refuses.
    New-CylinderWithOffsetHole $srcA 40 30 5 10 0
    $stdout = & $exe mirror-feature --input $srcA --plane right --out $copyA --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "mirror-feature (auto-pick / right / copy) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')           { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $copyA)           { throw "json path mismatch: '$($r.path)' vs '$copyA'" }
    if (-not (Test-Path $copyA))      { throw "mirrored copy not created: $copyA" }
    if (-not (Test-Path $srcA))       { throw "source should be preserved: $srcA" }
    if ($r.message -notmatch 'right') { throw "message should mention 'right': $($r.message)" }
    Write-Host ("[ok] mirror auto-pick / right / copy -> {0} ({1:N0} bytes)" -f $copyA, (Get-Item $copyA).Length)

    # ── happy: hole in +Y → mirror across Top plane (XZ, Y=0), case-insensitive ──
    New-CylinderWithOffsetHole $srcB 40 30 5 0 10
    $stdout = & $exe mirror-feature --input $srcB --plane TOP --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "mirror-feature (in-place / TOP) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.path -ne $srcB)            { throw "in-place path should equal input: '$($r.path)' vs '$srcB'" }
    Write-Host ("[ok] mirror auto-pick / TOP (case-insensitive) / in-place -> {0}" -f $srcB)

    # ── validation: unknown mirror plane (spec layer) ───────────────────────
    & $exe mirror-feature --input $srcA --plane left 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for 'left' (not a default plane)" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not recognized') { throw "error message missing 'not recognized': $errMsg" }
    Write-Host "[ok] validation rejects 'left' (not a default SW plane)"

    # ── validation: nonexistent input (spec layer) ──────────────────────────
    & $exe mirror-feature --input $missing --plane front 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')    { throw "error message missing 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    # ── SW layer: feature name that doesn't exist ───────────────────────────
    & $exe mirror-feature --input $srcA --plane right --feature "NoSuchFeature99" 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for unknown feature name" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'select feature') { throw "error message should reference feature selection: $errMsg" }
    Write-Host "[ok] SW layer rejects unknown feature name"

    Write-Host '[ok] M10-mirror all checks passed'
} finally {
    foreach ($f in @($srcA, $srcB, $copyA, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
