# L2 integration: M49 — driving dimensions on CATALOG helper parts
# (create_cylinder Ø / create_rectangular_block L+W / create_flange OD + center Ø).
#
# Before M49 these helpers drew raw circles/rectangles (no driving dims), so a
# resize orchestration could change a catalog part's LENGTH (extrude D1) but
# never its DIAMETER/footprint — the exact gap the resize E2E hit. Acceptance
# here is the in-place file edit: create → inspect (dim exists) →
# modify_feature --part the sketch dim → inspect bbox proves real rescale.
#
# Also guards the flange rule: bolt circles are deliberately NOT dimensioned
# (cut sketch carries exactly ONE dim — the center hole).
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M49-catalog-dims.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cyl = Join-Path $tmpDir ("m49_cyl_{0}.sldprt" -f $rand)
$blk = Join-Path $tmpDir ("m49_blk_{0}.sldprt" -f $rand)
$flg = Join-Path $tmpDir ("m49_flg_{0}.sldprt" -f $rand)

$script:fail = 0
function Check([string]$label, [bool]$cond, [string]$detail = '') {
    if ($cond) { Write-Host "[ok] $label" }
    else { Write-Host "[FAIL] $label $detail"; $script:fail++ }
}
function Run([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    $raw = ($o -join "`n")
    if ($LASTEXITCODE -ne 0) { throw "command failed: $($a -join ' ')`n$raw" }
    return $raw | ConvertFrom-Json
}
# All dims across features, flattened as "name=value" strings for matching.
function AllDims($info) {
    return @($info.data.features | ForEach-Object { $_.dimensions } | Where-Object { $_ })
}
function FeatDims($info, [string]$featName) {
    return @(($info.data.features | Where-Object { $_.name -eq $featName } | Select-Object -First 1).dimensions)
}

try {
    # ═══ 1. Cylinder: Ø is now a driving dim and editable in place ══════════
    Write-Host "== cylinder D40 L60: edit Ø in the saved file =="
    Run @('create-cylinder','--diameter','40','--length','60','--out',$cyl) | Out-Null
    $c0 = Run @('inspect-part','--input',$cyl)
    $cylSketchDims = FeatDims $c0 '草图1'
    Check "cylinder sketch carries the Ø40 driving dim" `
        (@($cylSketchDims | Where-Object { $_.value -eq 40 }).Count -eq 1) `
        (($cylSketchDims | ForEach-Object { "$($_.name)=$($_.value)" }) -join ',')
    Run @('modify-feature','--feature','D1@草图1','--value','70','--part',$cyl) | Out-Null
    $c1 = Run @('inspect-part','--input',$cyl)
    Check "cylinder Ø 40→70: bbox 70×70×60 (real rescale)" `
        (([Math]::Abs($c1.data.sizeMm.x - 70) -lt 0.1) -and
         ([Math]::Abs($c1.data.sizeMm.y - 70) -lt 0.1) -and
         ([Math]::Abs($c1.data.sizeMm.z - 60) -lt 0.1)) `
        "got $($c1.data.sizeMm.x)x$($c1.data.sizeMm.y)x$($c1.data.sizeMm.z)"

    # ═══ 2. Block: L + W are driving dims; edit L in place ══════════════════
    Write-Host "== block 80×50×20: edit length in the saved file =="
    Run @('create-rectangular-block','--length','80','--width','50','--height','20','--out',$blk) | Out-Null
    $b0 = Run @('inspect-part','--input',$blk)
    $blkSketchDims = FeatDims $b0 '草图1'
    Check "block sketch carries L80 + W50 driving dims" `
        ((@($blkSketchDims | Where-Object { $_.value -eq 80 }).Count -eq 1) -and
         (@($blkSketchDims | Where-Object { $_.value -eq 50 }).Count -eq 1)) `
        (($blkSketchDims | ForEach-Object { "$($_.name)=$($_.value)" }) -join ',')
    Run @('modify-feature','--feature','D1@草图1','--value','100','--part',$blk) | Out-Null
    $b1 = Run @('inspect-part','--input',$blk)
    Check "block L 80→100: bbox 100×50×20" `
        (([Math]::Abs($b1.data.sizeMm.x - 100) -lt 0.1) -and
         ([Math]::Abs($b1.data.sizeMm.y - 50) -lt 0.1) -and
         ([Math]::Abs($b1.data.sizeMm.z - 20) -lt 0.1)) `
        "got $($b1.data.sizeMm.x)x$($b1.data.sizeMm.y)x$($b1.data.sizeMm.z)"

    # ═══ 3. Flange: OD + center Ø dims; bolt circles stay undimensioned ═════
    Write-Host "== flange D80 t10 center30 4xM9 PCD55: OD edit + bolt-dim guard =="
    Run @('create-flange','--outer','80','--thickness','10','--center-hole','30',
          '--bolt-count','4','--bolt-d','9','--pcd','55','--out',$flg) | Out-Null
    $f0 = Run @('inspect-part','--input',$flg)
    $diskDims = FeatDims $f0 '草图1'
    $cutDims = FeatDims $f0 '草图2'
    Check "flange disk sketch carries the Ø80 OD dim" `
        (@($diskDims | Where-Object { $_.value -eq 80 }).Count -eq 1) `
        (($diskDims | ForEach-Object { "$($_.name)=$($_.value)" }) -join ',')
    Check "flange cut sketch has EXACTLY 1 dim (center Ø30; bolt circles undimensioned)" `
        ((@($cutDims).Count -eq 1) -and ($cutDims[0].value -eq 30)) `
        (($cutDims | ForEach-Object { "$($_.name)=$($_.value)" }) -join ',')
    Run @('modify-feature','--feature','D1@草图1','--value','100','--part',$flg) | Out-Null
    $f1 = Run @('inspect-part','--input',$flg)
    Check "flange OD 80→100: bbox 100×100×10" `
        (([Math]::Abs($f1.data.sizeMm.x - 100) -lt 0.1) -and
         ([Math]::Abs($f1.data.sizeMm.y - 100) -lt 0.1)) `
        "got $($f1.data.sizeMm.x)x$($f1.data.sizeMm.y)x$($f1.data.sizeMm.z)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M49 catalog driving dims -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($cyl, $blk, $flg)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
