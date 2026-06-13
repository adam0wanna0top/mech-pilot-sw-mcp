# L2 integration: M53-(1) component pose — add-component / inspect-assembly
# rotation. Proves the new --rotation-x/y/z parameters actually reorient an
# inserted component (read back via inspect-assembly's new 'orientation' axes)
# AND that rotation spins the part in place (positionMm unchanged vs un-rotated).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M53-component-rotation.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$block    = Join-Path $tmpDir ("rot_block_{0}.sldprt" -f (Get-Random))
$asmBase  = Join-Path $tmpDir ("rot_base_{0}.sldasm"  -f (Get-Random))
$asmZ     = Join-Path $tmpDir ("rot_z_{0}.sldasm"     -f (Get-Random))
$asmY     = Join-Path $tmpDir ("rot_y_{0}.sldasm"     -f (Get-Random))
$asmX     = Join-Path $tmpDir ("rot_x_{0}.sldasm"     -f (Get-Random))
$asmOff   = Join-Path $tmpDir ("rot_off_{0}.sldasm"   -f (Get-Random))
$errFile  = Join-Path $tmpDir 'stderr_m53.txt'

$tol = 0.001   # unit-vector axis tolerance

# Assert that a JSON axis array [x,y,z] equals expected within tolerance.
function Assert-Axis($axis, $ex, $ey, $ez, $label) {
    if ($null -eq $axis) { throw "$label axis missing" }
    if ([Math]::Abs([double]$axis[0] - $ex) -gt $tol -or
        [Math]::Abs([double]$axis[1] - $ey) -gt $tol -or
        [Math]::Abs([double]$axis[2] - $ez) -gt $tol) {
        throw ("$label expected ({0},{1},{2}) got ({3},{4},{5})" -f `
            $ex, $ey, $ez, $axis[0], $axis[1], $axis[2])
    }
}

function Get-OnlyComponent($asm) {
    $stdout = & $exe inspect-assembly --input $asm --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "inspect-assembly exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.data.componentCount -ne 1) {
        throw "expected 1 component, got $($r.data.componentCount)"
    }
    return $r.data.components[0]
}

try {
    # ── setup: a non-cubic block so axes are unambiguous, 4 fresh assemblies ──
    & $exe create-rectangular-block --length 60 --width 20 --height 20 --out $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }
    foreach ($a in @($asmBase, $asmZ, $asmY, $asmX, $asmOff)) {
        & $exe new-assembly --out $a --output json 2>$errFile | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "setup new-assembly $a failed: $(Get-Content $errFile -Raw)" }
    }

    # ── 1. baseline: insert with NO rotation → identity orientation ──────────
    & $exe add-component --assembly $asmBase --component $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "baseline add-component failed: $(Get-Content $errFile -Raw)" }
    $base = Get-OnlyComponent $asmBase
    if ($null -eq $base.orientation) { throw "orientation field missing from inspect output" }
    Assert-Axis $base.orientation.xAxis  1 0 0 "baseline xAxis"
    Assert-Axis $base.orientation.yAxis  0 1 0 "baseline yAxis"
    Assert-Axis $base.orientation.zAxis  0 0 1 "baseline zAxis"
    Write-Host "[ok] baseline (no rotation): identity orientation"

    # ── 2. rotate 90 deg about Z → local +X points to assembly +Y ────────────
    & $exe add-component --assembly $asmZ --component $block --rotation-z 90 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Z-rotation add-component failed: $(Get-Content $errFile -Raw)" }
    $rz = Get-OnlyComponent $asmZ
    Assert-Axis $rz.orientation.xAxis  0  1 0 "Z90 xAxis"
    Assert-Axis $rz.orientation.yAxis -1  0 0 "Z90 yAxis"
    Assert-Axis $rz.orientation.zAxis  0  0 1 "Z90 zAxis"
    # No jump: same frame-origin position as the un-rotated baseline.
    if ([Math]::Abs($rz.positionMm.x - $base.positionMm.x) -gt 0.01 -or
        [Math]::Abs($rz.positionMm.y - $base.positionMm.y) -gt 0.01 -or
        [Math]::Abs($rz.positionMm.z - $base.positionMm.z) -gt 0.01) {
        throw ("Z90 moved the part: base $($base.positionMm | ConvertTo-Json -Compress) vs rotated $($rz.positionMm | ConvertTo-Json -Compress)")
    }
    Write-Host "[ok] rotate 90 about Z: xAxis->(0,1,0), position unchanged (spin in place)"

    # ── 3. rotate 90 deg about Y → local +X points to assembly -Z ────────────
    & $exe add-component --assembly $asmY --component $block --rotation-y 90 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Y-rotation add-component failed: $(Get-Content $errFile -Raw)" }
    $ry = Get-OnlyComponent $asmY
    Assert-Axis $ry.orientation.xAxis  0 0 -1 "Y90 xAxis"
    Assert-Axis $ry.orientation.yAxis  0 1  0 "Y90 yAxis"
    Assert-Axis $ry.orientation.zAxis  1 0  0 "Y90 zAxis"
    Write-Host "[ok] rotate 90 about Y: xAxis->(0,0,-1), zAxis->(1,0,0)"

    # ── 4. rotate 90 deg about X → local +Y points to assembly +Z ────────────
    & $exe add-component --assembly $asmX --component $block --rotation-x 90 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "X-rotation add-component failed: $(Get-Content $errFile -Raw)" }
    $rx = Get-OnlyComponent $asmX
    Assert-Axis $rx.orientation.xAxis  1  0 0 "X90 xAxis"
    Assert-Axis $rx.orientation.yAxis  0  0 1 "X90 yAxis"
    Assert-Axis $rx.orientation.zAxis  0 -1 0 "X90 zAxis"
    Write-Host "[ok] rotate 90 about X: yAxis->(0,0,1), zAxis->(0,-1,0)"

    # ── 5. rotation at an OFFSET position: orient AND keep position ──────────
    & $exe add-component --assembly $asmOff --component $block --position-x 50 --position-y 10 --rotation-z 90 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "offset+rotation add-component failed: $(Get-Content $errFile -Raw)" }
    $off = Get-OnlyComponent $asmOff
    Assert-Axis $off.orientation.xAxis 0 1 0 "offset Z90 xAxis"
    if ([Math]::Abs($off.positionMm.x - 50) -gt 0.01 -or
        [Math]::Abs($off.positionMm.y - 10) -gt 0.01) {
        throw "offset positionMm X/Y should be (50,10), got $($off.positionMm | ConvertTo-Json -Compress)"
    }
    Write-Host "[ok] offset (50,10)+Z90: oriented xAxis->(0,1,0) AND position held at (50,10)"

    # ── 6. negative: out-of-range angle is rejected with a DEGREES hint ──────
    & $exe add-component --assembly $asmBase --component $block --rotation-x 5000 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for 5000-degree rotation" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'DEGREES') { throw "error should hint DEGREES (not radians): $errMsg" }
    Write-Host "[ok] validation rejects 5000 deg with DEGREES hint"

    Write-Host '[ok] M53-component-rotation all checks passed'
} finally {
    foreach ($f in @($block, $asmBase, $asmZ, $asmY, $asmX, $asmOff, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
