# L2 integration: M46 — generic sketch primitives carry DRIVING dimensions.
#
# sketch_circle now adds a Ø dimension; sketch_rectangle_center adds width +
# height. So the size lives as a real dimension that modify_feature (M45) can
# edit and the downstream feature follows — true geometric resizing of our parts.
# (AddDimension2 needs swInputDimValOnCreate OFF or it pops a modal dialog — M46.)
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M46-dimensioned-sketches.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }
$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random

$script:fail = 0
function Check([string]$label, [bool]$cond, [string]$detail = '') {
    if ($cond) { Write-Host "[ok] $label" } else { Write-Host "[FAIL] $label $detail"; $script:fail++ }
}
function Run([string[]]$a) {
    $o = & $exe @a --output json 2>&1; $raw = ($o -join "`n")
    if ($LASTEXITCODE -ne 0) { throw "command failed: $($a -join ' ')`n$raw" }
    $obj = $null; try { $obj = $raw | ConvertFrom-Json } catch {}
    return $obj
}
function SK($obj) { if ($obj.message -match "sketch name='([^']+)'") { return $Matches[1] } ; throw "no sketch: $($obj.message)" }
function SketchDims($name) {
    $d = (Run @('inspect-active')).data
    $sk = $d.features | Where-Object { $_.name -eq $name }
    return @($sk.dimensions)
}
function CloseDoc { $p = Join-Path $tmpDir ("m46_{0}_{1}.sldprt" -f (Get-Random), $rand); Run @('save-part','--out',$p) | Out-Null; Remove-Item $p -Force -EA SilentlyContinue }

try {
    Write-Host "== circle: driving Ø dimension, editable, geometry follows =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    $cd = @(SketchDims $s1)
    Check "circle has 1 driving dim" ($cd.Count -eq 1) "got $($cd.Count)"
    Check "Ø dim == 40 mm (r20)" (($cd[0].value -eq 40) -and ($cd[0].unit -eq 'mm')) "got $($cd[0].value)$($cd[0].unit)"
    # edit the diameter via modify_feature (M45), then extrude — geometry must follow
    Run @('modify-feature','--feature',$cd[0].name,'--value','60') | Out-Null
    Check "Ø now 60 after edit" ((@(SketchDims $s1))[0].value -eq 60) "got $((@(SketchDims $s1))[0].value)"
    Run @('extrude','--sketch',$s1,'--depth','30') | Out-Null
    $g = (Run @('inspect-active')).data
    Check "extrude reflects Ø60: bbox 60x60x30" (($g.sizeMm.x -eq 60) -and ($g.sizeMm.y -eq 60) -and ($g.sizeMm.z -eq 30)) "got $($g.sizeMm.x)x$($g.sizeMm.y)x$($g.sizeMm.z)"
    CloseDoc

    Write-Host "== rectangle: driving width + height dimensions, editable, geometry follows =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-rectangle-center','--cx','0','--cy','0','--corner-x','40','--corner-y','30') | Out-Null
    $s2 = SK (Run @('end-sketch'))
    $rd = @(SketchDims $s2)
    Check "rectangle has 2 driving dims" ($rd.Count -eq 2) "got $($rd.Count)"
    $vals = @($rd | ForEach-Object { $_.value } | Sort-Object)
    Check "dims are 60 and 80" (($vals[0] -eq 60) -and ($vals[1] -eq 80)) "got $($vals -join ',')"
    # edit the 80 dim -> 100, extrude; bbox must show 100 x 60
    $w = ($rd | Where-Object { $_.value -eq 80 })[0]
    Run @('modify-feature','--feature',$w.name,'--value','100') | Out-Null
    Run @('extrude','--sketch',$s2,'--depth','10') | Out-Null
    $g2 = (Run @('inspect-active')).data
    $okxy = (($g2.sizeMm.x -eq 100) -and ($g2.sizeMm.y -eq 60)) -or (($g2.sizeMm.x -eq 60) -and ($g2.sizeMm.y -eq 100))
    Check "extrude reflects edited width: bbox 100x60x10" ($okxy -and ($g2.sizeMm.z -eq 10)) "got $($g2.sizeMm.x)x$($g2.sizeMm.y)x$($g2.sizeMm.z)"
    CloseDoc

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M46 dimensioned-sketches -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    Get-ChildItem $tmpDir -Filter ("m46_*_{0}.sldprt" -f $rand) -EA SilentlyContinue | Remove-Item -Force -EA SilentlyContinue
}
