# L2 integration: pattern-circular should rotate-pattern a single seed feature
# around the part's ±Z axis. Uses cylinder + add_axial_hole as seed (cylinder
# provides the axial-Z cylindrical face that mark=1 latches onto, unlike
# blocks which have no central axis surface).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M22-pattern-circular.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$srcFull = Join-Path $tmpDir ("cirpat_full_{0}.sldprt"  -f (Get-Random))
$srcHalf = Join-Path $tmpDir ("cirpat_half_{0}.sldprt"  -f (Get-Random))
$srcCopy = Join-Path $tmpDir ("cirpat_copy_{0}.sldprt"  -f (Get-Random))
$srcEmpty = Join-Path $tmpDir ("cirpat_empty_{0}.sldprt" -f (Get-Random))
$srcBlock = Join-Path $tmpDir ("cirpat_block_{0}.sldprt" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

function New-CylinderWithOffsetHole([string]$path, [double]$dia, [double]$len, [double]$holeDia, [double]$offX, [double]$offY) {
    & $exe create-cylinder --diameter $dia --length $len --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup create-cylinder failed: $(Get-Content $errFile -Raw)" }
    & $exe add-axial-hole --input $path --diameter $holeDia --position-x $offX --position-y $offY --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-axial-hole failed: $(Get-Content $errFile -Raw)" }
    if (-not (Test-Path $path)) { throw "setup part not created: $path" }
}

try {
    # ── happy: full 360° circle, 6 instances PCD20 on D40 cylinder, in-place ──
    #   Cylinder D40 L20 + Φ5 hole at (10, 0) = PCD20 → pattern 6× → 6 holes
    #   spaced 60° around the central Z axis. Seed hole is at radius 10 mm,
    #   well inside cylinder radius 20 mm.
    New-CylinderWithOffsetHole $srcFull 40 20 5 10 0
    $stdout = & $exe pattern-circular --input $srcFull --count 6 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "pattern-circular (full 360 / 6×) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $srcFull)           { throw "in-place path should equal input: '$($r.path)' vs '$srcFull'" }
    if ($r.message -notmatch 'full circle') { throw "message should mention 'full circle': $($r.message)" }
    Write-Host ("[ok] full-circle 6x on D40 cylinder (in-place) -> {0}" -f $srcFull)

    # ── happy: 180° arc, 3 instances on D40 cylinder, save as copy ──────────
    #   Cylinder D40 L20 + Φ4 hole at (8, 0) → pattern 3× over 180° → 3 holes
    #   at 0°, 60°, 120° (60° per instance = 180/3).
    New-CylinderWithOffsetHole $srcHalf 40 20 4 8 0
    $stdout = & $exe pattern-circular --input $srcHalf --count 3 --angle 180 --out $srcCopy --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "pattern-circular (180 / 3x) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.path -ne $srcCopy)           { throw "json path mismatch: '$($r.path)' vs '$srcCopy'" }
    if (-not (Test-Path $srcCopy))      { throw "patterned copy not created: $srcCopy" }
    if (-not (Test-Path $srcHalf))      { throw "source should be preserved: $srcHalf" }
    if ($r.message -notmatch 'arc')     { throw "message should mention 'arc': $($r.message)" }
    Write-Host ("[ok] 180-deg arc 3x on D40 cylinder (copy) -> {0} ({1:N0} bytes)" -f $srcCopy, (Get-Item $srcCopy).Length)

    # ── SW layer: cylinder w/o user features (no seed) should reject cleanly ──
    & $exe create-cylinder --diameter 30 --length 20 --out $srcEmpty --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup empty cylinder failed: $(Get-Content $errFile -Raw)" }
    & $exe pattern-circular --input $srcEmpty --count 4 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit: empty cylinder has no seed feature" }
    $errMsg = Get-Content $errFile -Raw
    # Tool either rejects with "no user-meaningful features" (preferred) or
    # the SW layer null-returns on FCP3 — both are acceptable failure modes.
    if ($errMsg -notmatch 'feature') { throw "error should reference feature/seed: $errMsg" }
    Write-Host "[ok] empty cylinder rejected (no seed feature)"

    # ── SW layer: pure block has no axial-Z cylindrical face → reject ──────
    #   Note: a block *with* an add_axial_hole'd hole actually does have a
    #   Z-axial cylindrical face (the hole's inner wall) — that scenario
    #   would silent-fail elsewhere (SW degenerate "pattern a hole around
    #   its own axis"). Here we test the pure-block case which exercises
    #   the FindFirstAxialCylinderFace null-return path directly.
    & $exe create-rectangular-block --length 40 --width 40 --height 20 --out $srcBlock --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }

    & $exe pattern-circular --input $srcBlock --count 4 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit: pure block has no axial-Z cylindrical face" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'cylindrical face') { throw "error should reference cylindrical face: $errMsg" }
    Write-Host "[ok] pure block rejected (no axial-Z cylindrical face)"

    # ── validation: count below min (spec layer) ────────────────────────────
    & $exe pattern-circular --input $srcFull --count 1 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for count=1" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'count')      { throw "error should reference count: $errMsg" }
    Write-Host "[ok] validation rejects count=1 (seed-only is a no-op)"

    Write-Host '[ok] M22-pattern-circular all checks passed'
} finally {
    foreach ($f in @($srcFull, $srcHalf, $srcCopy, $srcEmpty, $srcBlock, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
