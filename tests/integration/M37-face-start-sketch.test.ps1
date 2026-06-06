# L2 integration: M37 — face-based start_sketch ("+z"/"-z"/"+x"/... selectors).
#
# Closes E2E gap #2 (M35): sketching on a body face required first creating a
# ref plane at that exact height. Now start_sketch accepts a face selector and
# picks the EXTREME planar face with that outward normal.
#
#   Test 1: build the M35 bracket (plate + boss + bore) using ONLY "+z" face
#           selectors — no add_ref_plane. The 2nd "+z" must resolve to the BOSS
#           top (Z=30, now the outermost +Z face), not the plate top (Z=10) —
#           the through-bore going through the boss proves extreme-face picking.
#           Expect bbox 80x80x30 (boss extruded +Z off the face), 1 body, 9 faces.
#   Test 2: "+z" on an empty part (no body) is rejected with a clear message.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M37-face-start-sketch.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$part = Join-Path $tmpDir ("m37_face_{0}.sldprt" -f $rand)

$script:fail = 0
function Check([string]$label, [bool]$cond, [string]$detail = '') {
    if ($cond) { Write-Host "[ok] $label" }
    else { Write-Host "[FAIL] $label $detail"; $script:fail++ }
}
function Run([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    $raw = ($o -join "`n")
    if ($LASTEXITCODE -ne 0) { throw "command failed: $($a -join ' ')`n$raw" }
    $obj = $null; try { $obj = $raw | ConvertFrom-Json } catch {}
    return $obj
}
function TryRun([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($o -join "`n") }
}
function SK($obj) { if ($obj.message -match "sketch name='([^']+)'") { return $Matches[1] } ; throw "no sketch name: $($obj.message)" }

try {
    Write-Host "== Test 1: bracket via '+z' face selectors (no ref planes) =="
    Run @('new-part') | Out-Null

    # plate 80x80x10 (plane-based, the base)
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-rectangle-center','--cx','0','--cy','0','--corner-x','40','--corner-y','40') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s1,'--depth','10') | Out-Null

    # boss: sketch on the "+z" face (= plate top, Z=10) — NO ref plane
    $f1 = Run @('start-sketch','--plane','+z')
    Check "start_sketch '+z' on plate top succeeds" ($f1.message -match 'face') $f1.message
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s2 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s2,'--depth','20') | Out-Null

    # bore: sketch on the "+z" face again (must now be the BOSS top, Z=30)
    Run @('start-sketch','--plane','+z') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','10') | Out-Null
    $s3 = SK (Run @('end-sketch'))
    Run @('extrude-cut','--sketch',$s3,'--depth','40') | Out-Null

    Run @('save-part','--out',$part) | Out-Null
    $i = (Run @('inspect-part','--input',$part)).data
    Check "bracket: 1 solid body" ($i.bodyCount -eq 1) "got $($i.bodyCount)"
    Check "bracket: bbox 80x80x30 (boss extruded +Z off the face)" (($i.sizeMm.x -eq 80) -and ($i.sizeMm.y -eq 80) -and ($i.sizeMm.z -eq 30)) "got $($i.sizeMm.x)x$($i.sizeMm.y)x$($i.sizeMm.z)"
    Check "bracket: 9 faces (bore went through the boss => 2nd '+z' picked boss top)" ($i.totalFaceCount -eq 9) "got $($i.totalFaceCount)"

    Write-Host "== Test 2: '+z' on an empty part is rejected with guidance =="
    Run @('new-part') | Out-Null
    $bad = TryRun @('start-sketch','--plane','+z')
    Check "empty-part '+z' exits non-zero" ($bad.Code -ne 0) "code=$($bad.Code)"
    Check "empty-part '+z' error mentions no body/face" (($bad.Out -match 'no') -and ($bad.Out -match 'face')) $bad.Out
    # close the leaked empty doc
    $discard = Join-Path $tmpDir ("m37_discard_{0}.sldprt" -f $rand)
    # an empty part can't be saved as a meaningful body; just try, ignore result
    TryRun @('save-part','--out',$discard) | Out-Null
    if (Test-Path $discard) { Remove-Item $discard -Force -EA SilentlyContinue }

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M37 face-based start_sketch -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    if (Test-Path $part) { Remove-Item $part -Force -ErrorAction SilentlyContinue }
}
