# L2 integration: M54 topology-addressed coincident/distance mates. Proves the
# new face1Index/face2Index on add_mate_coincident / add_mate_distance select a
# SPECIFIC planar face (by inspect_topology index) and that the mate physically
# moves the free component by the predicted amount. Also: reference-plane
# keywords still work (back-compat), and negatives (non-planar / out-of-range).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M54-topo-plane-mate.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$b1   = Join-Path $tmpDir ("pm_b1_{0}.sldprt"  -f (Get-Random))
$b2   = Join-Path $tmpDir ("pm_b2_{0}.sldprt"  -f (Get-Random))
$cyl  = Join-Path $tmpDir ("pm_cyl_{0}.sldprt" -f (Get-Random))
$asmC = Join-Path $tmpDir ("pm_coin_{0}.sldasm" -f (Get-Random))
$asmD = Join-Path $tmpDir ("pm_dist_{0}.sldasm" -f (Get-Random))
$asmK = Join-Path $tmpDir ("pm_keyw_{0}.sldasm" -f (Get-Random))
$asmN = Join-Path $tmpDir ("pm_neg_{0}.sldasm"  -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr_m54.txt'

$tol = 1.0  # mm — mate-solve tolerance on the predicted move

function Inspect($a) {
    $stdout = & $exe inspect-assembly --input $a --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-assembly exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return ($stdout | ConvertFrom-Json).data
}
function PosZ($a, $src) { return [double]((Inspect $a).components | Where-Object { $_.sourcePath -eq $src }).positionMm.z }
function Names($a, $src) { return ((Inspect $a).components | Where-Object { $_.sourcePath -eq $src }).name }

try {
    # ── setup: two identical blocks (distinct files) + a cylinder ───────────
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $b1 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "block1 failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $b2 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "block2 failed: $(Get-Content $errFile -Raw)" }
    & $exe create-cylinder --diameter 20 --length 30 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "cyl failed: $(Get-Content $errFile -Raw)" }

    # ── read block topology: the +Z (top) and -Z (bottom) planar faces ──────
    $topoOut = & $exe inspect-topology --part $b1 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-topology block failed: $(Get-Content $errFile -Raw)" }
    $faces = ($topoOut | ConvertFrom-Json).data.faces
    $topF = $faces | Where-Object { $_.type -eq 'plane' -and [double]$_.normal.z -gt 0.9 } | Select-Object -First 1
    $botF = $faces | Where-Object { $_.type -eq 'plane' -and [double]$_.normal.z -lt -0.9 } | Select-Object -First 1
    if ($null -eq $topF -or $null -eq $botF) { throw "could not find +Z/-Z planar faces on the block" }
    $topIdx = [int]$topF.index
    $botIdx = [int]$botF.index
    # a non-planar (cylindrical) face on the cylinder, for the negative case
    $cylOut = & $exe inspect-topology --part $cyl --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-topology cyl failed: $(Get-Content $errFile -Raw)" }
    $cylF = ($cylOut | ConvertFrom-Json).data.faces | Where-Object { $_.type -eq 'cylinder' } | Select-Object -First 1
    $cylIdx = [int]$cylF.index
    Write-Host ("[ok] topology: block top face #{0}, bottom face #{1}; cyl cylinder face #{2}" -f $topIdx, $botIdx, $cylIdx)

    # ── 1. COINCIDENT by face index: block2 bottom -> block1 top ────────────
    #   block1 fixed @ z=0 (top face world z=+5); block2 free @ z=40 (bottom
    #   face world z=35). Coincident drops block2 bottom to z=5 → moves -30.
    & $exe new-assembly --out $asmC --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmC --component $b1 --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmC --component $b2 --position-z 40 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "coin setup failed: $(Get-Content $errFile -Raw)" }
    $beforeZ = PosZ $asmC $b2
    $n1 = Names $asmC $b1
    $n2 = Names $asmC $b2
    $stdout = & $exe add-mate-coincident --assembly $asmC `
        --component1 $n1 --face1-index $topIdx `
        --component2 $n2 --face2-index $botIdx `
        --alignment closest --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "coincident-by-face failed: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.message -notmatch "#$topIdx plane" -or $r.message -notmatch "#$botIdx plane") {
        throw "message should echo both face signatures: $($r.message)"
    }
    $afterZ = PosZ $asmC $b2
    $moved = $beforeZ - $afterZ
    if ([Math]::Abs($moved - 30) -gt $tol) {
        throw ("coincident: block2 should drop 30 mm (bottom->top), moved {0:N2} (z {1:N2}->{2:N2})" -f $moved, $beforeZ, $afterZ)
    }
    Write-Host ("[ok] coincident by face index: block2 dropped {0:N1} mm onto block1 top (faces coplanar)" -f $moved)

    # ── 2. DISTANCE by face index: block2 bottom 20 mm above block1 top ─────
    #   block2 bottom @35 → target 5+20=25 → moves -10.
    & $exe new-assembly --out $asmD --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmD --component $b1 --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmD --component $b2 --position-z 40 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dist setup failed: $(Get-Content $errFile -Raw)" }
    $beforeZ = PosZ $asmD $b2
    $n1 = Names $asmD $b1
    $n2 = Names $asmD $b2
    & $exe add-mate-distance --assembly $asmD `
        --component1 $n1 --face1-index $topIdx `
        --component2 $n2 --face2-index $botIdx `
        --distance 20 --alignment closest --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "distance-by-face failed: $(Get-Content $errFile -Raw)" }
    $afterZ = PosZ $asmD $b2
    $moved = $beforeZ - $afterZ
    if ([Math]::Abs($moved - 10) -gt $tol) {
        throw ("distance 20: block2 should move 10 mm (35->25), moved {0:N2} (z {1:N2}->{2:N2})" -f $moved, $beforeZ, $afterZ)
    }
    Write-Host ("[ok] distance 20 by face index: block2 moved {0:N1} mm (bottom 20 mm above block1 top)" -f $moved)

    # ── 3. back-compat: reference-plane KEYWORDS still mate ─────────────────
    & $exe new-assembly --out $asmK --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmK --component $b1 --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmK --component $b2 --position-x 80 --output json 2>$errFile | Out-Null
    $n1 = Names $asmK $b1
    $n2 = Names $asmK $b2
    $stdout = & $exe add-mate-coincident --assembly $asmK `
        --component1 $n1 --plane1 top --component2 $n2 --plane2 top `
        --alignment aligned --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "keyword coincident failed: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.message -notmatch 'top@') { throw "keyword path should echo 'top@...': $($r.message)" }
    Write-Host "[ok] back-compat: reference-plane keyword mate still works"

    # ── 4. negative: a non-planar (cylinder) face index is rejected ─────────
    & $exe new-assembly --out $asmN --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmN --component $b1 --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmN --component $cyl --position-z 40 --output json 2>$errFile | Out-Null
    $n1 = Names $asmN $b1
    $nc = Names $asmN $cyl
    & $exe add-mate-coincident --assembly $asmN `
        --component1 $n1 --face1-index $topIdx `
        --component2 $nc --face2-index $cylIdx 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for a cylindrical face in a coincident mate" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not a plane') { throw "error should say 'not a plane': $errMsg" }
    Write-Host "[ok] non-planar (cylinder) face index rejected ('not a plane')"

    # ── 5. negative: out-of-range face index lists the valid range ──────────
    & $exe add-mate-coincident --assembly $asmN `
        --component1 $n1 --face1-index 999 --component2 $nc --plane2 front 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for out-of-range face index" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'out of range') { throw "error should say 'out of range': $errMsg" }
    Write-Host "[ok] out-of-range face index rejected (lists valid range)"

    Write-Host '[ok] M54-topo-plane-mate all checks passed'
} finally {
    foreach ($f in @($b1, $b2, $cyl, $asmC, $asmD, $asmK, $asmN, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
