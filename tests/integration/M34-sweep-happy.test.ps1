# L2 integration: M34 — sweep HAPPY case (LANDMARK 4 sweep).
#
# Third M33 misdiagnosis corrected: sweep does NOT need the RPC-faulting
# CreateDefinition(swFmSweep=17)+AccessSelections path, and does NOT need a
# recorded macro. The simple 14-arg InsertProtrusionSwept works given:
#   • selection marks: profile = mark 1, path = mark 4 (loft uses mark=1 for
#     all; sweep does NOT — that mark reuse is why M32 silently failed), and
#   • geometry: profile plane ⊥ path start direction, path starts at profile.
#
#   Test 1 straight: Top-plane circle (⊥ Y) profile + Front-plane Y-line path
#       → D10×50 pipe. Verify 1 body / 3 faces / 2 edges / bbox 10×50×10.
#   Test 2 elbow:    same profile + Front-plane quarter-arc path
#       → clean curved tube (1 body / 3 faces / 2 edges), proving sweep
#       handles curved paths (the real differentiator vs extrude).
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M34-sweep-happy.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$straightPart = Join-Path $tmpDir ("m34_sweep_straight_{0}.sldprt" -f $rand)
$elbowPart = Join-Path $tmpDir ("m34_sweep_elbow_{0}.sldprt" -f $rand)

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

try {
    # ═══════════════════════════════════════════════════════════════════════
    # Test 1 — straight sweep: D10 circle profile (Top plane) along a 50mm
    #          Y-line path (Front plane) → straight pipe
    # ═══════════════════════════════════════════════════════════════════════
    Write-Host "== Test 1: straight sweep (D10 x 50 pipe) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','top') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','5') | Out-Null
    $p1 = SK (Run @('end-sketch'))
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-line','--x1','0','--y1','0','--x2','0','--y2','50') | Out-Null
    $path1 = SK (Run @('end-sketch'))
    $sw1 = Run @('sweep','--profile',$p1,'--path',$path1)
    Check "straight sweep returns a feature" ($sw1.message -match 'feature') $sw1.message
    Run @('save-part','--out',$straightPart) | Out-Null
    $i1 = (Run @('inspect-part','--input',$straightPart)).data
    Check "straight: 1 solid body" ($i1.bodyCount -eq 1) "got $($i1.bodyCount)"
    Check "straight: bbox 10x50x10" (($i1.sizeMm.x -eq 10) -and ($i1.sizeMm.y -eq 50) -and ($i1.sizeMm.z -eq 10)) "got $($i1.sizeMm.x)x$($i1.sizeMm.y)x$($i1.sizeMm.z)"
    Check "straight: 3 faces (1 side + 2 caps)" ($i1.totalFaceCount -eq 3) "got $($i1.totalFaceCount)"
    Check "straight: 2 edges (2 end circles)" ($i1.totalEdgeCount -eq 2) "got $($i1.totalEdgeCount)"

    # ═══════════════════════════════════════════════════════════════════════
    # Test 2 — curved sweep: same profile along a quarter-arc path → elbow
    # ═══════════════════════════════════════════════════════════════════════
    Write-Host "== Test 2: curved elbow sweep (quarter-arc path) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','top') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','5') | Out-Null
    $p2 = SK (Run @('end-sketch'))
    Run @('start-sketch','--plane','front') | Out-Null
    # quarter arc: center (25,0), start (0,0) tangent +Y, end (25,25) tangent +X, CW
    Run @('sketch-arc-center','--cx','25','--cy','0','--x1','0','--y1','0','--x2','25','--y2','25','--direction','-1') | Out-Null
    $path2 = SK (Run @('end-sketch'))
    $sw2 = Run @('sweep','--profile',$p2,'--path',$path2)
    Check "elbow sweep returns a feature" ($sw2.message -match 'feature') $sw2.message
    Run @('save-part','--out',$elbowPart) | Out-Null
    $i2 = (Run @('inspect-part','--input',$elbowPart)).data
    Check "elbow: 1 solid body" ($i2.bodyCount -eq 1) "got $($i2.bodyCount)"
    Check "elbow: 3 faces / 2 edges (clean swept tube)" (($i2.totalFaceCount -eq 3) -and ($i2.totalEdgeCount -eq 2)) "got $($i2.totalFaceCount)f/$($i2.totalEdgeCount)e"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M34 sweep happy case -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($straightPart, $elbowPart)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
