# L2 integration: M45 — modify_feature edits ANY surfaced dimension.
#
# featureName now accepts a full dimension name from inspect_* editableDimensions
# (e.g. "D1@凸台-拉伸2") — not just a feature's primary — and the unit (mm/deg) is
# auto-detected from the dimension's own type. A bare feature name still maps to
# "D1@<feature>" (backward compatible).
#
#   2-extrude part: edit the SECOND extrude by full dim name; edit the first by
#                   bare feature name (-> D1); revolve: edit angle by full dim
#                   name (deg auto-detected); unknown dim -> rejected.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M45-modify-any-dim.test.ps1

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
function TryRun([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($o -join "`n") }
}
function SK($obj) { if ($obj.message -match "sketch name='([^']+)'") { return $Matches[1] } ; throw "no sketch: $($obj.message)" }
# Features that carry an editable dimension (an extrude is 'Extrusion' on a plane,
# but 'ICE' when sketched on a face — match by having a dim, not by type).
function Exts($d) { return @($d.features | Where-Object { @($_.dimensions).Count -gt 0 }) }
function DimVal($f) { return (@($f.dimensions))[0].value }
function DimName($f) { return (@($f.dimensions))[0].name }
function CloseDoc { $p = Join-Path $tmpDir ("m45_{0}_{1}.sldprt" -f (Get-Random), $rand); Run @('save-part','--out',$p) | Out-Null; Remove-Item $p -Force -EA SilentlyContinue }

try {
    Write-Host "== 2-extrude part: edit 2nd extrude by FULL dim name, 1st by bare feature name =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s1,'--depth','30') | Out-Null
    Run @('start-sketch','--plane','+z') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','10') | Out-Null
    $s2 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s2,'--depth','15') | Out-Null

    $d = (Run @('inspect-active')).data
    $exts = Exts $d
    Check "two extrude features" ($exts.Count -eq 2) "got $($exts.Count)"
    $e1Name = $exts[0].name
    $e2DimName = DimName $exts[1]      # full surfaced name, e.g. D1@凸台-拉伸2
    Check "boss (2nd extrude) starts at 15" ((DimVal $exts[1]) -eq 15) "got $(DimVal $exts[1])"

    # edit the SECOND extrude by its FULL dimension name (the new M45 capability)
    $m = Run @('modify-feature','--feature',$e2DimName,'--value','25')
    Check "modify echoes the dim name" ($m.message -match [regex]::Escape($e2DimName)) $m.message
    $e2b = (Exts (Run @('inspect-active')).data)[1]
    Check "boss now 25 (edited by full dim name)" ((DimVal $e2b) -eq 25) "got $(DimVal $e2b)"

    # edit the FIRST extrude by BARE feature name (backward compat -> D1@<feature>)
    Run @('modify-feature','--feature',$e1Name,'--value','40') | Out-Null
    $e1c = (Exts (Run @('inspect-active')).data)[0]
    Check "base now 40 (bare feature name -> D1)" ((DimVal $e1c) -eq 40) "got $(DimVal $e1c)"
    CloseDoc

    Write-Host "== revolve: edit angle by full dim name (degrees auto-detected) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-line','--x1','0','--y1','0','--x2','20','--y2','0') | Out-Null
    Run @('sketch-line','--x1','20','--y1','0','--x2','20','--y2','30') | Out-Null
    Run @('sketch-line','--x1','20','--y1','30','--x2','0','--y2','30') | Out-Null
    Run @('sketch-line','--x1','0','--y1','30','--x2','0','--y2','0') | Out-Null
    Run @('sketch-centerline','--x1','0','--y1','0','--x2','0','--y2','30') | Out-Null
    $rs = SK (Run @('end-sketch'))
    Run @('revolve','--sketch',$rs,'--angle','360') | Out-Null
    $dr = (Run @('inspect-active')).data
    $rev = (@($dr.features | Where-Object { $_.typeName -eq 'Revolution' }))[0]
    $revDimName = DimName $rev
    Check "full revolve = 3 faces" ($dr.totalFaceCount -eq 3) "got $($dr.totalFaceCount)"
    Run @('modify-feature','--feature',$revDimName,'--value','180') | Out-Null
    $dr2 = (Run @('inspect-active')).data
    Check "angle edited via full dim name -> 4 faces (deg auto-detected)" ($dr2.totalFaceCount -eq 4) "got $($dr2.totalFaceCount)"
    CloseDoc

    Write-Host "== negative: unknown dimension is rejected =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','10') | Out-Null
    $z = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$z,'--depth','10') | Out-Null
    $bad = TryRun @('modify-feature','--feature','D9@NoSuchFeature','--value','5')
    Check "unknown dim rejected (non-zero)" ($bad.Code -ne 0) "code=$($bad.Code)"
    Check "error mentions editable dimension" ($bad.Out -match 'editable dimension') $bad.Out
    CloseDoc

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M45 modify-any-dim -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    Get-ChildItem $tmpDir -Filter ("m45_*_{0}.sldprt" -f $rand) -EA SilentlyContinue | Remove-Item -Force -EA SilentlyContinue
}
