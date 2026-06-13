# L2 integration: M56 add_mate_tangent + concentric any-axis auto-pick.
# Proves (a) a tangent mate makes a cylinder's curved face touch a plane (the
# cylinder moves so it just contacts), (b) tangent rejects two planar faces,
# and (c) concentric auto-pick now finds a cylinder on a ROTATED part (axis Y),
# not just axis-Z.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M56-tangent-mate.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$plate = Join-Path $tmpDir ("tn_plate_{0}.sldprt" -f (Get-Random))
$cyl   = Join-Path $tmpDir ("tn_cyl_{0}.sldprt"   -f (Get-Random))
$cylA  = Join-Path $tmpDir ("tn_ca_{0}.sldprt"    -f (Get-Random))
$cylB  = Join-Path $tmpDir ("tn_cb_{0}.sldprt"    -f (Get-Random))
$asmTan = Join-Path $tmpDir ("tn_tan_{0}.sldasm"  -f (Get-Random))
$asmNeg = Join-Path $tmpDir ("tn_neg_{0}.sldasm"  -f (Get-Random))
$asmCon = Join-Path $tmpDir ("tn_con_{0}.sldasm"  -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr_m56.txt'

$tol = 0.5

function Inspect($a) {
    $stdout = & $exe inspect-assembly --input $a --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-assembly exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return ($stdout | ConvertFrom-Json).data
}
function Topo($p) {
    $stdout = & $exe inspect-topology --part $p --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-topology exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return ($stdout | ConvertFrom-Json).data.faces
}

try {
    # ── parts: a thin vertical plate (+X face), a Z-cylinder, two cylinders ──
    & $exe create-rectangular-block --length 10 --width 100 --height 100 --out $plate --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "plate failed: $(Get-Content $errFile -Raw)" }
    & $exe create-cylinder --diameter 40 --length 60 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "cyl failed: $(Get-Content $errFile -Raw)" }
    & $exe create-cylinder --diameter 30 --length 50 --out $cylA --output json 2>$errFile | Out-Null
    & $exe create-cylinder --diameter 30 --length 50 --out $cylB --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "cylA/B failed: $(Get-Content $errFile -Raw)" }

    # plate +X planar face (normal +X) and cyl OD cylindrical face
    $plateFaces = Topo $plate
    $pxF = $plateFaces | Where-Object { $_.type -eq 'plane' -and [double]$_.normal.x -gt 0.9 } | Select-Object -First 1
    if ($null -eq $pxF) { throw "plate +X face not found" }
    $pxIdx = [int]$pxF.index
    $plateFlat = $plateFaces | Where-Object { $_.type -eq 'plane' } | Select-Object -First 1
    $plateFlatIdx = [int]$plateFlat.index
    $cylFaces = Topo $cyl
    $cylOD = $cylFaces | Where-Object { $_.type -eq 'cylinder' } | Select-Object -First 1
    $cylODIdx = [int]$cylOD.index
    $cylEnd = $cylFaces | Where-Object { $_.type -eq 'plane' } | Select-Object -First 1
    $cylEndIdx = [int]$cylEnd.index
    Write-Host ("[ok] topology: plate +X face #{0}, cyl OD #{1}" -f $pxIdx, $cylODIdx)

    # ── 1. TANGENT: Z-cylinder OD tangent to the plate's +X face ────────────
    #   plate +X face at x=5; cyl placed at x=40 → tangent pulls cyl axis to
    #   x=5+20=25, so the cyl just touches the plate (cyl minX -> 5).
    & $exe new-assembly --out $asmTan --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmTan --component $plate --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmTan --component $cyl --position-x 40 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "tan setup failed: $(Get-Content $errFile -Raw)" }
    $d = Inspect $asmTan
    $pn = ($d.components | Where-Object { $_.sourcePath -eq $plate }).name
    $cn = ($d.components | Where-Object { $_.sourcePath -eq $cyl }).name
    $stdout = & $exe add-mate-tangent --assembly $asmTan `
        --component1 $cn --face1-index $cylODIdx `
        --component2 $pn --face2-index $pxIdx `
        --alignment closest --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "tangent mate failed: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.message -notmatch "#$cylODIdx cylinder") { throw "message should echo the cyl face signature: $($r.message)" }
    $cylBox = (Inspect $asmTan).components | Where-Object { $_.sourcePath -eq $cyl }
    if ([Math]::Abs($cylBox.worldBoundingBoxMm.minX - 5) -gt $tol) {
        throw "tangent: cyl should touch plate +X face at x=5, got minX=$($cylBox.worldBoundingBoxMm.minX)"
    }
    Write-Host "[ok] tangent: cylinder OD rests on the plate +X face (cyl minX -> 5)"

    # ── 2. negative: two PLANAR faces is not a tangent ──────────────────────
    & $exe new-assembly --out $asmNeg --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmNeg --component $plate --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmNeg --component $cyl --position-x 40 --output json 2>$errFile | Out-Null
    $d = Inspect $asmNeg
    $pn = ($d.components | Where-Object { $_.sourcePath -eq $plate }).name
    $cn = ($d.components | Where-Object { $_.sourcePath -eq $cyl }).name
    & $exe add-mate-tangent --assembly $asmNeg `
        --component1 $cn --face1-index $cylEndIdx `
        --component2 $pn --face2-index $plateFlatIdx 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for two planar faces" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'curved') { throw "error should say 'at least one curved face': $errMsg" }
    Write-Host "[ok] tangent rejects two planar faces ('needs at least one curved')"

    # ── 3. concentric auto-pick on ROTATED parts (axis Y, M56 fallback) ─────
    #   two cylinders both rotated to axis Y → auto-pick used to require axis-Z
    #   and would error; now it falls back to any cylinder and the mate works.
    & $exe new-assembly --out $asmCon --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmCon --component $cylA --rotation-x 90 --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmCon --component $cylB --position-x 60 --rotation-x 90 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "concentric setup failed: $(Get-Content $errFile -Raw)" }
    $d = Inspect $asmCon
    $na = ($d.components | Where-Object { $_.sourcePath -eq $cylA }).name
    $nb = ($d.components | Where-Object { $_.sourcePath -eq $cylB }).name
    $stdout = & $exe add-mate-concentric --assembly $asmCon `
        --component1 $na --component2 $nb --alignment closest --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "concentric on rotated (axis-Y) parts failed: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.message -notmatch 'cylinder') { throw "concentric should auto-pick a cylinder: $($r.message)" }
    Write-Host "[ok] concentric auto-pick now finds a cylinder on a rotated (axis-Y) part"

    Write-Host '[ok] M56-tangent-mate all checks passed'
} finally {
    foreach ($f in @($plate, $cyl, $cylA, $cylB, $asmTan, $asmNeg, $asmCon, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
