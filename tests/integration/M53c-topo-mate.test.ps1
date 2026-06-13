# L2 integration: M53-(3) topology-addressed concentric mate. Proves
# add_mate_concentric's new face1Index/face2Index actually target a SPECIFIC
# cylindrical face: auto-pick centers a pin on the flange axis (0,0), while
# addressing a bolt-hole face by its inspect_topology index moves the pin to
# THAT hole's (x,y). Plus negative cases (plane index / out-of-range).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M53c-topo-mate.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$flange  = Join-Path $tmpDir ("topo_flange_{0}.sldprt" -f (Get-Random))
$pin     = Join-Path $tmpDir ("topo_pin_{0}.sldprt"    -f (Get-Random))
$asmAuto = Join-Path $tmpDir ("topo_auto_{0}.sldasm"   -f (Get-Random))
$asmIdx  = Join-Path $tmpDir ("topo_idx_{0}.sldasm"    -f (Get-Random))
$asmNeg  = Join-Path $tmpDir ("topo_neg_{0}.sldasm"    -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr_m53c.txt'

$posTol = 0.2  # mm — mate-solve + frame-origin round-off

function Inspect($a) {
    $stdout = & $exe inspect-assembly --input $a --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-assembly exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return ($stdout | ConvertFrom-Json).data
}
function PinPos($a, $pinPath) {
    $d = Inspect $a
    return ($d.components | Where-Object { $_.sourcePath -eq $pinPath }).positionMm
}
function Names($a, $flangePath, $pinPath) {
    $d = Inspect $a
    $fn = ($d.components | Where-Object { $_.sourcePath -eq $flangePath }).name
    $pn = ($d.components | Where-Object { $_.sourcePath -eq $pinPath }).name
    return @($fn, $pn)
}
function SetupAsm($a) {
    & $exe new-assembly --out $a --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "new-assembly $a failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $a --component $flange --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "add flange to $a failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $a --component $pin --position-z 40 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "add pin to $a failed: $(Get-Content $errFile -Raw)" }
}

try {
    # ── setup: a 4-bolt flange + a pin sized to the bolt holes ──────────────
    & $exe create-flange --outer 80 --thickness 10 --center-hole 20 `
        --bolt-count 4 --bolt-d 8 --pcd 55 --out $flange --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "create-flange failed: $(Get-Content $errFile -Raw)" }
    & $exe create-cylinder --diameter 8 --length 20 --out $pin --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "create-cylinder failed: $(Get-Content $errFile -Raw)" }

    # ── read the flange topology; find a bolt-hole cylinder (r~4, off-center)
    #    and a plane face (for the negative case) ─────────────────────────────
    $topo = & $exe inspect-topology --part $flange --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-topology failed: $(Get-Content $errFile -Raw)" }
    $faces = ($topo | ConvertFrom-Json).data.faces

    $boltHole = $faces | Where-Object {
        $_.type -eq 'cylinder' -and [Math]::Abs([double]$_.radiusMm - 4) -lt 0.3 -and
        ([Math]::Abs([double]$_.axisOriginMm.x) -gt 1 -or [Math]::Abs([double]$_.axisOriginMm.y) -gt 1)
    } | Select-Object -First 1
    if ($null -eq $boltHole) { throw "no bolt-hole cylinder (r~4, off-center) found in flange topology" }
    $holeIdx = [int]$boltHole.index
    $holeX = [double]$boltHole.axisOriginMm.x
    $holeY = [double]$boltHole.axisOriginMm.y
    if ([Math]::Sqrt($holeX*$holeX + $holeY*$holeY) -lt 20) { throw "bolt hole not on the PCD (got r=$([Math]::Sqrt($holeX*$holeX+$holeY*$holeY)))" }

    $planeFace = $faces | Where-Object { $_.type -eq 'plane' } | Select-Object -First 1
    if ($null -eq $planeFace) { throw "no plane face found in flange topology" }
    $planeIdx = [int]$planeFace.index
    Write-Host ("[ok] topology: bolt-hole face #{0} at ({1:N1},{2:N1}); plane face #{3}" -f $holeIdx, $holeX, $holeY, $planeIdx)

    # ── 1. AUTO-PICK (back-compat): pin centers on the flange axis (0,0) ────
    SetupAsm $asmAuto
    $n = Names $asmAuto $flange $pin
    & $exe add-mate-concentric --assembly $asmAuto `
        --component1 $n[0] --component2 $n[1] --alignment closest --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "auto-pick mate failed: $(Get-Content $errFile -Raw)" }
    $p = PinPos $asmAuto $pin
    if ([Math]::Abs($p.x) -gt $posTol -or [Math]::Abs($p.y) -gt $posTol) {
        throw "auto-pick: pin should center at (0,0), got ($($p.x),$($p.y))"
    }
    Write-Host "[ok] auto-pick: pin centered on flange axis (0,0)"

    # ── 2. TOPOLOGY INDEX: pin moves to the addressed bolt hole (x,y) ───────
    SetupAsm $asmIdx
    $n = Names $asmIdx $flange $pin
    $stdout = & $exe add-mate-concentric --assembly $asmIdx `
        --component1 $n[0] --face1-index $holeIdx --component2 $n[1] `
        --alignment closest --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "indexed mate failed: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.message -notmatch "#$holeIdx") { throw "message should echo the face signature '#$holeIdx': $($r.message)" }
    $p = PinPos $asmIdx $pin
    if ([Math]::Abs($p.x - $holeX) -gt $posTol -or [Math]::Abs($p.y - $holeY) -gt $posTol) {
        throw ("indexed mate: pin should sit at bolt hole ({0:N2},{1:N2}), got ({2:N2},{3:N2})" -f $holeX, $holeY, $p.x, $p.y)
    }
    Write-Host ("[ok] face #{0} addressed: pin moved to bolt hole ({1:N1},{2:N1}) — NOT the axis" -f $holeIdx, $p.x, $p.y)

    # ── 3. negative: a plane face index is rejected (concentric needs a cyl) ─
    SetupAsm $asmNeg
    $n = Names $asmNeg $flange $pin
    & $exe add-mate-concentric --assembly $asmNeg `
        --component1 $n[0] --face1-index $planeIdx --component2 $n[1] 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for a plane face index" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not a cylinder') { throw "error should say 'not a cylinder': $errMsg" }
    Write-Host "[ok] plane face index rejected ('not a cylinder')"

    # ── 4. negative: out-of-range face index lists the valid range ──────────
    & $exe add-mate-concentric --assembly $asmNeg `
        --component1 $n[0] --face1-index 9999 --component2 $n[1] 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for out-of-range face index" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'out of range') { throw "error should say 'out of range': $errMsg" }
    Write-Host "[ok] out-of-range face index rejected (lists valid range)"

    Write-Host '[ok] M53c-topo-mate all checks passed'
} finally {
    foreach ($f in @($flange, $pin, $asmAuto, $asmIdx, $asmNeg, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
