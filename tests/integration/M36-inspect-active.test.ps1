# L2 integration: M36 — inspect_active (read active doc mid-build, no save/close).
#
# Solves the M35 E2E "blind build" gap. The critical property to verify is that
# inspect_active does NOT close the active doc — so the LLM can inspect, then
# KEEP building. The test builds a cylinder, inspects mid-build, then (proving
# the doc is still open) cuts a bore, inspects again to see the change, and
# finally save_part + inspect_part to confirm the two tools agree.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M36-inspect-active.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$part = Join-Path $tmpDir ("m36_inspect_active_{0}.sldprt" -f $rand)

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
function SK($obj) { if ($obj.message -match "sketch name='([^']+)'") { return $Matches[1] } ; throw "no sketch name: $($obj.message)" }
function PL($obj) { if ($obj.message -match "plane '([^']+)'") { return $Matches[1] } ; throw "no plane name: $($obj.message)" }

try {
    Write-Host "== Test 1: inspect_active mid-build (cylinder D40 x 30) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s1,'--depth','30') | Out-Null

    $a1 = (Run @('inspect-active')).data
    Check "inspect_active #1: 1 body" ($a1.bodyCount -eq 1) "got $($a1.bodyCount)"
    Check "inspect_active #1: bbox 40x40x30" (($a1.sizeMm.x -eq 40) -and ($a1.sizeMm.y -eq 40) -and ($a1.sizeMm.z -eq 30)) "got $($a1.sizeMm.x)x$($a1.sizeMm.y)x$($a1.sizeMm.z)"
    Check "inspect_active #1: 3 faces (plain cylinder)" ($a1.totalFaceCount -eq 3) "got $($a1.totalFaceCount)"

    Write-Host "== Test 2: doc stays OPEN — keep building (bore a hole) =="
    # If inspect_active had closed the doc, these would fail with 'no active doc'.
    $rp = PL (Run @('add-ref-plane','--source','front','--distance','30'))
    Run @('start-sketch','--plane',$rp) | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','5') | Out-Null
    $s2 = SK (Run @('end-sketch'))
    Run @('extrude-cut','--sketch',$s2,'--depth','40') | Out-Null
    Check "doc stayed open after inspect_active (bore cut succeeded)" $true

    $a2 = (Run @('inspect-active')).data
    Check "inspect_active #2: still 1 body" ($a2.bodyCount -eq 1) "got $($a2.bodyCount)"
    Check "inspect_active #2: 4 faces after bore (3 -> 4)" ($a2.totalFaceCount -eq 4) "got $($a2.totalFaceCount)"
    Check "inspect_active #2: 4 edges after bore (2 -> 4)" ($a2.totalEdgeCount -eq 4) "got $($a2.totalEdgeCount)"

    Write-Host "== Test 3: inspect_active agrees with inspect_part after save =="
    Run @('save-part','--out',$part) | Out-Null
    $p = (Run @('inspect-part','--input',$part)).data
    Check "inspect_part faces == inspect_active #2 faces" ($p.totalFaceCount -eq $a2.totalFaceCount) "part=$($p.totalFaceCount) active=$($a2.totalFaceCount)"
    Check "inspect_part bbox == inspect_active #2 bbox" (($p.sizeMm.x -eq $a2.sizeMm.x) -and ($p.sizeMm.z -eq $a2.sizeMm.z)) "part=$($p.sizeMm.x)x$($p.sizeMm.z) active=$($a2.sizeMm.x)x$($a2.sizeMm.z)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M36 inspect_active -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    if (Test-Path $part) { Remove-Item $part -Force -ErrorAction SilentlyContinue }
}
