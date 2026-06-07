# L2 integration: M39 — editable dimensions in inspect_part / inspect_active.
#
# PartMetadata now lists each feature's display dimensions as {name, value, unit}
# so an LLM can SEE what modify_feature can change. name is the "D1@<feature>"
# handle modify_feature consumes (extrude/cut depth -> mm, revolve angle -> deg);
# (since M46, generic circle/rectangle sketches carry their own driving dimension.)
#
#   Test 1 extrude: depth dim "D1@<ext>" = 30 mm; the SURFACED name round-trips
#                   through modify_feature (30 -> 50) and the re-read dim shows 50
#                   (the see <-> edit loop, the whole point of PR-1).
#   Test 2 revolve: angle dim = 360 deg (angular -> degrees, not mm).
#   Test 3 consistency: inspect_part (saved) agrees with inspect_active.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M39-part-dimensions.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$part = Join-Path $tmpDir ("m39_dims_{0}.sldprt" -f $rand)
$revPart = Join-Path $tmpDir ("m39_rev_{0}.sldprt" -f $rand)

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
function Feat($d, $type) { return (@($d.features | Where-Object { $_.typeName -eq $type }))[0] }

try {
    Write-Host "== Test 1: extrude depth dim (mm) + name round-trips via modify_feature =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s1,'--depth','30') | Out-Null
    $d1 = (Run @('inspect-active')).data
    Check "editableDimensionCount == 2 (sketch Ø + extrude depth)" ($d1.editableDimensionCount -eq 2) "got $($d1.editableDimensionCount)"
    $ext = Feat $d1 'Extrusion'
    $extDims = @($ext.dimensions)
    Check "extrude feature has 1 dimension" ($extDims.Count -eq 1) "got $($extDims.Count)"
    Check "dim name == D1@<feature>" ($extDims[0].name -eq "D1@$($ext.name)") "got $($extDims[0].name)"
    Check "dim value == 30" ($extDims[0].value -eq 30) "got $($extDims[0].value)"
    Check "dim unit == mm" ($extDims[0].unit -eq 'mm') "got $($extDims[0].unit)"
    $sk = Feat $d1 'ProfileFeature'
    Check "sketch carries its driving Ø dimension (M46)" (@($sk.dimensions).Count -eq 1) "got $(@($sk.dimensions).Count)"
    Check "sketch Ø dim == 40 (r20)" (@($sk.dimensions)[0].value -eq 40) "got $(@($sk.dimensions)[0].value)"

    # The surfaced dim names this feature; modify_feature edits it; re-read shows new value.
    Run @('modify-feature','--feature',$ext.name,'--value','50') | Out-Null
    $extb = Feat (Run @('inspect-active')).data 'Extrusion'
    Check "after modify: dim value == 50 (see <-> edit loop)" (@($extb.dimensions)[0].value -eq 50) "got $(@($extb.dimensions)[0].value)"
    Run @('save-part','--out',$part) | Out-Null

    Write-Host "== Test 2: revolve angle dim (deg, not mm) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-line','--x1','0','--y1','0','--x2','20','--y2','0') | Out-Null
    Run @('sketch-line','--x1','20','--y1','0','--x2','20','--y2','30') | Out-Null
    Run @('sketch-line','--x1','20','--y1','30','--x2','0','--y2','30') | Out-Null
    Run @('sketch-line','--x1','0','--y1','30','--x2','0','--y2','0') | Out-Null
    Run @('sketch-centerline','--x1','0','--y1','0','--x2','0','--y2','30') | Out-Null
    $rs = SK (Run @('end-sketch'))
    Run @('revolve','--sketch',$rs,'--angle','360') | Out-Null
    $rev = Feat (Run @('inspect-active')).data 'Revolution'
    $revDims = @($rev.dimensions)
    Check "revolve feature has 1 dimension" ($revDims.Count -eq 1) "got $($revDims.Count)"
    Check "angle dim value == 360" ($revDims[0].value -eq 360) "got $($revDims[0].value)"
    Check "angle dim unit == deg" ($revDims[0].unit -eq 'deg') "got $($revDims[0].unit)"
    Run @('save-part','--out',$revPart) | Out-Null

    Write-Host "== Test 3: inspect_part (saved) agrees with inspect_active =="
    $p = (Run @('inspect-part','--input',$part)).data
    $pext = Feat $p 'Extrusion'
    Check "inspect_part dim name == D1@<feature>" (@($pext.dimensions)[0].name -eq "D1@$($pext.name)") "got $(@($pext.dimensions)[0].name)"
    Check "inspect_part shows modified value 50" (@($pext.dimensions)[0].value -eq 50) "got $(@($pext.dimensions)[0].value)"
    Check "inspect_part editableDimensionCount == 2" ($p.editableDimensionCount -eq 2) "got $($p.editableDimensionCount)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M39 part-dimensions -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    if (Test-Path $part) { Remove-Item $part -Force -ErrorAction SilentlyContinue }
    if (Test-Path $revPart) { Remove-Item $revPart -Force -ErrorAction SilentlyContinue }
}
