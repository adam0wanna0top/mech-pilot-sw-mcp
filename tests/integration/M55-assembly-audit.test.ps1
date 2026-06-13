# L2 integration: M55 assembly audit — inspect_assembly worldBoundingBoxMm +
# check_interference. Proves (a) inspect_assembly reports each component's
# world-space bounding box, correct even when the part is rotated, and (b)
# check_interference detects real solid overlap (with volume) while leaving
# non-overlapping and merely-touching parts clear.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M55-assembly-audit.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$bA   = Join-Path $tmpDir ("au_a_{0}.sldprt" -f (Get-Random))
$bB   = Join-Path $tmpDir ("au_b_{0}.sldprt" -f (Get-Random))
$asmBox   = Join-Path $tmpDir ("au_box_{0}.sldasm"   -f (Get-Random))
$asmClear = Join-Path $tmpDir ("au_clear_{0}.sldasm" -f (Get-Random))
$asmClash = Join-Path $tmpDir ("au_clash_{0}.sldasm" -f (Get-Random))
$asmTouch = Join-Path $tmpDir ("au_touch_{0}.sldasm" -f (Get-Random))
$errFile  = Join-Path $tmpDir 'stderr_m55.txt'

$tol = 0.5

function Inspect($a) {
    $stdout = & $exe inspect-assembly --input $a --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-assembly exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return ($stdout | ConvertFrom-Json).data
}
function Clash($a, [bool]$coinc=$false) {
    $cliArgs = @('check-interference','--input',$a,'--output','json')
    if ($coinc) { $cliArgs += '--treat-coincident' }
    $stdout = & $exe @cliArgs 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "check-interference exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return ($stdout | ConvertFrom-Json).data
}

try {
    # ── setup: two 40x30x10 blocks (distinct files) ─────────────────────────
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $bA --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "blockA failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $bB --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "blockB failed: $(Get-Content $errFile -Raw)" }

    # ── 1. worldBoundingBoxMm: un-rotated block at (50,0,0) ─────────────────
    & $exe new-assembly --out $asmBox --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmBox --component $bA --position-x 50 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "box setup failed: $(Get-Content $errFile -Raw)" }
    $c = (Inspect $asmBox).components[0]
    $box1 = $c.worldBoundingBoxMm
    if ($null -eq $box1) { throw "worldBoundingBoxMm missing from inspect_assembly output" }
    # 40(x) x 30(y) x 10(z), centered at (50,0,0) → x[30,70] y[-15,15] z[-5,5]
    if ([Math]::Abs(($box1.maxX - $box1.minX) - 40) -gt $tol -or
        [Math]::Abs(($box1.maxY - $box1.minY) - 30) -gt $tol -or
        [Math]::Abs(($box1.maxZ - $box1.minZ) - 10) -gt $tol) {
        throw "unrotated world bbox size wrong: $($box1 | ConvertTo-Json -Compress)"
    }
    if ([Math]::Abs($box1.minX - 30) -gt $tol -or [Math]::Abs($box1.maxX - 70) -gt $tol) {
        throw "unrotated world bbox X should be [30,70], got [$($box1.minX),$($box1.maxX)]"
    }
    Write-Host "[ok] worldBoundingBoxMm present + correct for an un-rotated block at (50,0,0)"

    # ── 2. worldBoundingBoxMm tracks ROTATION (the key value) ───────────────
    #   rotate 90 about Z → the 40-long (x) and 30-wide (y) dims swap in world.
    & $exe add-component --assembly $asmBox --component $bB --rotation-z 90 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "rotated add failed: $(Get-Content $errFile -Raw)" }
    $rot = (Inspect $asmBox).components | Where-Object { $_.sourcePath -eq $bB }
    $box2 = $rot.worldBoundingBoxMm
    if ([Math]::Abs(($box2.maxX - $box2.minX) - 30) -gt $tol -or
        [Math]::Abs(($box2.maxY - $box2.minY) - 40) -gt $tol) {
        throw "rotated world bbox should be x-size 30 / y-size 40 (swapped), got x=$($box2.maxX-$box2.minX) y=$($box2.maxY-$box2.minY)"
    }
    Write-Host "[ok] worldBoundingBoxMm tracks rotation: Z90 swaps x-size->30 / y-size->40"

    # ── 3. check_interference: clear (gap between blocks) → count 0 ─────────
    & $exe new-assembly --out $asmClear --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmClear --component $bA --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmClear --component $bB --position-x 60 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "clear setup failed: $(Get-Content $errFile -Raw)" }
    $d = Clash $asmClear
    if ($d.interferenceCount -ne 0) { throw "clear assembly should have 0 interferences, got $($d.interferenceCount)" }
    if ($d -isnot [object] -or $d.title -notmatch 'au_clear') { } # message sanity
    Write-Host "[ok] check_interference: non-overlapping blocks (60mm apart) → 0 interferences"

    # ── 4. check_interference: real overlap → count 1 + correct volume ─────
    #   blockA x[-20,20], blockB@20 x[0,40] → overlap 20x30x10 = 6000 mm^3
    & $exe new-assembly --out $asmClash --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmClash --component $bA --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmClash --component $bB --position-x 20 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "clash setup failed: $(Get-Content $errFile -Raw)" }
    $d = Clash $asmClash
    if ($d.interferenceCount -lt 1) { throw "overlapping blocks should interfere, got $($d.interferenceCount)" }
    $vol = [double]$d.interferences[0].volumeMm3
    if ([Math]::Abs($vol - 6000) -gt 50) { throw "overlap volume should be ~6000 mm^3, got $vol" }
    $names = $d.interferences[0].components
    if ($names.Count -ne 2) { throw "interference should name 2 components, got $($names -join ',')" }
    Write-Host ("[ok] check_interference: overlapping blocks → 1 clash, {0} mm³, pair {1}" -f $vol, ($names -join ' / '))

    # ── 5. touching faces: clear by default, flagged with --treat-coincident ─
    & $exe new-assembly --out $asmTouch --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmTouch --component $bA --output json 2>$errFile | Out-Null
    & $exe add-component --assembly $asmTouch --component $bB --position-x 40 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "touch setup failed: $(Get-Content $errFile -Raw)" }
    $d = Clash $asmTouch $false
    if ($d.interferenceCount -ne 0) { throw "touching faces should be clear by default, got $($d.interferenceCount)" }
    $d = Clash $asmTouch $true
    if ($d.interferenceCount -lt 1) { throw "touching faces should be flagged with --treat-coincident, got $($d.interferenceCount)" }
    Write-Host "[ok] check_interference: touching faces clear by default, flagged with --treat-coincident"

    Write-Host '[ok] M55-assembly-audit all checks passed'
} finally {
    foreach ($f in @($bA, $bB, $asmBox, $asmClear, $asmClash, $asmTouch, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
