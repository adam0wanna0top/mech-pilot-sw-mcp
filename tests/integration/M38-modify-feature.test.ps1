# L2 integration: M38 — modify_feature (edit an existing feature's dimension
# on the active part + regenerate). The "mechanical Cursor" edit primitive.
#
#   Test 1 extrude depth: cylinder extrude 30 -> modify to 50 -> bbox Z 30->50 (decisive).
#   Test 2 revolve angle: full cylinder (360, 3 faces) -> modify to 180 -> 4 faces
#           (half-cylinder gains the flat diameter face) (decisive).
#   Test 3 cut depth: through-bore (4 faces) -> modify cut depth to blind -> 5 faces
#           (blind bore gains a flat bottom), still 1 body (cut routes through the
#           same IExtrudeFeatureData branch as Test 1).
#   Test 4 negative: modify a non-existent feature is rejected with guidance.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M38-modify-feature.test.ps1

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
function FeatName($d, $type) { return (@($d.features | Where-Object { $_.typeName -eq $type }))[0].name }
function CloseDoc([string]$tag) { $p = Join-Path $tmpDir ("m38_{0}_{1}.sldprt" -f $tag, $rand); TryRun @('save-part','--out',$p) | Out-Null; if (Test-Path $p) { Remove-Item $p -Force -EA SilentlyContinue } }

try {
    Write-Host "== Test 1: modify extrude depth (30 -> 50) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s1,'--depth','30') | Out-Null
    $d1 = (Run @('inspect-active')).data
    Check "before: bbox Z == 30" ($d1.sizeMm.z -eq 30) "got $($d1.sizeMm.z)"
    $ext = FeatName $d1 'Extrusion'
    $m = Run @('modify-feature','--feature',$ext,'--value','50')
    Check "modify reports the new value" ($m.message -match '50 mm') $m.message
    $d2 = (Run @('inspect-active')).data
    Check "after: bbox Z == 50 (regenerated)" ($d2.sizeMm.z -eq 50) "got $($d2.sizeMm.z)"
    Check "after: still 1 body" ($d2.bodyCount -eq 1) "got $($d2.bodyCount)"
    CloseDoc 't1'

    Write-Host "== Test 2: modify revolve angle (360 -> 180) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-line','--x1','0','--y1','0','--x2','20','--y2','0') | Out-Null
    Run @('sketch-line','--x1','20','--y1','0','--x2','20','--y2','30') | Out-Null
    Run @('sketch-line','--x1','20','--y1','30','--x2','0','--y2','30') | Out-Null
    Run @('sketch-line','--x1','0','--y1','30','--x2','0','--y2','0') | Out-Null
    Run @('sketch-centerline','--x1','0','--y1','0','--x2','0','--y2','30') | Out-Null
    $rs = SK (Run @('end-sketch'))
    Run @('revolve','--sketch',$rs,'--angle','360') | Out-Null
    $r1 = (Run @('inspect-active')).data
    Check "before: full cylinder 3 faces" ($r1.totalFaceCount -eq 3) "got $($r1.totalFaceCount)"
    $rev = FeatName $r1 'Revolution'
    $m2 = Run @('modify-feature','--feature',$rev,'--value','180')
    Check "modify reports the new angle" ($m2.message -match '180') $m2.message
    $r2 = (Run @('inspect-active')).data
    Check "after: half cylinder 4 faces (gained flat face)" ($r2.totalFaceCount -eq 4) "got $($r2.totalFaceCount)"
    CloseDoc 't2'

    Write-Host "== Test 3: modify cut depth (through -> blind) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $cs = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$cs,'--depth','30') | Out-Null
    Run @('start-sketch','--plane','+z') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','5') | Out-Null
    $bs = SK (Run @('end-sketch'))
    Run @('extrude-cut','--sketch',$bs,'--depth','40') | Out-Null   # through bore
    $b1 = (Run @('inspect-active')).data
    Check "before: through-bore 4 faces" ($b1.totalFaceCount -eq 4) "got $($b1.totalFaceCount)"
    $cut = FeatName $b1 'ICE'
    Run @('modify-feature','--feature',$cut,'--value','10') | Out-Null   # blind 10mm
    $b2 = (Run @('inspect-active')).data
    Check "after: blind bore 5 faces (gained flat bottom)" ($b2.totalFaceCount -eq 5) "got $($b2.totalFaceCount)"
    Check "after: still 1 body" ($b2.bodyCount -eq 1) "got $($b2.bodyCount)"
    CloseDoc 't3'

    Write-Host "== Test 4: modify a non-existent feature is rejected =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','10') | Out-Null
    $z = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$z,'--depth','10') | Out-Null
    $bad = TryRun @('modify-feature','--feature','NoSuchFeature','--value','5')
    Check "nonexistent feature exits non-zero" ($bad.Code -ne 0) "code=$($bad.Code)"
    Check "error mentions no editable dimension" ($bad.Out -match 'editable dimension') $bad.Out
    CloseDoc 't4'

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M38 modify_feature -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    Get-ChildItem $tmpDir -Filter ("m38_*_{0}.sldprt" -f $rand) -EA SilentlyContinue | Remove-Item -Force -EA SilentlyContinue
}
