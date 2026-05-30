# L2 integration: pattern-linear should one- and two-dimensional pattern a
# single seed feature on a rectangular block (block provides straight edges
# that FeatureLinearPattern2's mark=1 direction edge can latch onto, unlike
# cylinders/flanges which only have circular edges).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M12-pattern-linear.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$srcA    = Join-Path $tmpDir ("linpat_src_a_{0}.sldprt"  -f (Get-Random))
$srcB    = Join-Path $tmpDir ("linpat_src_b_{0}.sldprt"  -f (Get-Random))
$cyl     = Join-Path $tmpDir ("linpat_cyl_{0}.sldprt"    -f (Get-Random))
$copyA   = Join-Path $tmpDir ("linpat_copy_a_{0}.sldprt" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

function New-BlockWithOffsetHole([string]$path, [double]$l, [double]$w, [double]$h, [double]$holeDia, [double]$offX, [double]$offY) {
    & $exe create-rectangular-block --length $l --width $w --height $h --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup create-rectangular-block failed: $(Get-Content $errFile -Raw)" }
    & $exe add-axial-hole --input $path --diameter $holeDia --position-x $offX --position-y $offY --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-axial-hole failed: $(Get-Content $errFile -Raw)" }
    if (-not (Test-Path $path)) { throw "setup part not created: $path" }
}

try {
    # ── happy: 1D pattern, 3 holes along X axis, spacing 20 mm, save as copy ──
    #   Block 100×50×20 + Φ5 hole at (-40, 0) → pattern x×3 spacing 20 →
    #   holes at x = -40, -20, 0 (all inside the 100 mm x-extent [-50, 50]).
    New-BlockWithOffsetHole $srcA 100 50 20 5 -40 0
    $stdout = & $exe pattern-linear --input $srcA --axis1 x --count1 3 --spacing1 20 --out $copyA --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "pattern-linear (1D / x×3) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $copyA)             { throw "json path mismatch: '$($r.path)' vs '$copyA'" }
    if (-not (Test-Path $copyA))        { throw "patterned copy not created: $copyA" }
    if (-not (Test-Path $srcA))         { throw "source should be preserved: $srcA" }
    Write-Host ("[ok] 1D pattern x×3 spacing 20 on block (copy) -> {0} ({1:N0} bytes)" -f $copyA, (Get-Item $copyA).Length)

    # ── happy: 2D pattern, 3×2 grid (x spacing 25, y spacing 15), in-place ──
    #   Block 100×100×20 + Φ5 hole at (-40, -40) → 3×2 → 6 holes at
    #   (-40, -40), (-15, -40), (10, -40), (-40, -25), (-15, -25), (10, -25)
    #   all inside [-50, 50]×[-50, 50].
    New-BlockWithOffsetHole $srcB 100 100 20 5 -40 -40
    $stdout = & $exe pattern-linear --input $srcB --axis1 x --count1 3 --spacing1 25 --axis2 y --count2 2 --spacing2 15 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "pattern-linear (2D / 3×2) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.path -ne $srcB)              { throw "in-place path should equal input: '$($r.path)' vs '$srcB'" }
    # Note: avoid Unicode '×' in regex — PowerShell 5.x console encoding can
    # mangle multi-byte chars during stdout capture (the tool emits it fine).
    if ($r.message -notmatch 'grid')    { throw "message should mention 'grid': $($r.message)" }
    Write-Host ("[ok] 2D pattern 3×2 (x:25, y:15) on block (in-place) -> {0}" -f $srcB)

    # ── SW layer: cylinder has no straight edges → tool should reject ───────
    & $exe create-cylinder --diameter 30 --length 50 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cylinder failed: $(Get-Content $errFile -Raw)" }
    & $exe add-axial-hole --input $cyl --diameter 5 --position-x 8 --position-y 0 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup hole-in-cylinder failed: $(Get-Content $errFile -Raw)" }

    & $exe pattern-linear --input $cyl --axis1 x --count1 3 --spacing1 5 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit: cylinder has no straight edge along x" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'straight edge') { throw "error should reference straight edge: $errMsg" }
    Write-Host "[ok] cylinder rejected (no straight edge for direction)"

    # ── validation: unrecognized axis (spec layer) ──────────────────────────
    & $exe pattern-linear --input $srcA --axis1 w --count1 3 --spacing1 10 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for axis 'w'" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not recognized') { throw "error should reference 'not recognized': $errMsg" }
    Write-Host "[ok] validation rejects axis 'w'"

    # ── validation: count below min (spec layer) ────────────────────────────
    & $exe pattern-linear --input $srcA --axis1 x --count1 1 --spacing1 10 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for count=1" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'countDir1')  { throw "error should reference countDir1: $errMsg" }
    Write-Host "[ok] validation rejects count=1 (seed-only is a no-op)"

    Write-Host '[ok] M12-pattern-linear all checks passed'
} finally {
    foreach ($f in @($srcA, $srcB, $cyl, $copyA, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
