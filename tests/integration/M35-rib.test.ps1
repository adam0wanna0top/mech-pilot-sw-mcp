# L2 integration: M35 — rib (stiffener / gusset) HAPPY case.
#
# rib had been deferred since M27 as a "1-2 day scary exploration" (v1 hit
# "selection 不识"). Same story as the M34 cut/sweep misdiagnoses: reflecting
# InsertRib's signature + correct geometry + the standard rib options worked
# on the first sensible parameter combo. InsertRib returns void, so the tool
# detects success by the rib-feature count delta and auto-detects fill direction.
#
#   Test: L-bracket (Front-plane L-profile extruded +Z 30) + a gusset rib
#         (diagonal line on a mid plane Z=15 across the inner corner).
#         Plain L-bracket = 8 faces / 18 edges; after the gusset rib = 11 / 27.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M35-rib.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$ribPart = Join-Path $tmpDir ("m35_rib_{0}.sldprt" -f $rand)

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
    Write-Host "== M35: rib gusset in an L-bracket =="
    Run @('new-part') | Out-Null

    # L-profile on Front Plane: horizontal leg y0..8, vertical leg x0..8, inner corner (8,8)
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-line','--x1','0','--y1','0','--x2','40','--y2','0') | Out-Null
    Run @('sketch-line','--x1','40','--y1','0','--x2','40','--y2','8') | Out-Null
    Run @('sketch-line','--x1','40','--y1','8','--x2','8','--y2','8') | Out-Null
    Run @('sketch-line','--x1','8','--y1','8','--x2','8','--y2','40') | Out-Null
    Run @('sketch-line','--x1','8','--y1','40','--x2','0','--y2','40') | Out-Null
    Run @('sketch-line','--x1','0','--y1','40','--x2','0','--y2','0') | Out-Null
    $lp = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$lp,'--depth','30') | Out-Null

    # mid plane + diagonal rib line across the inner corner
    $rp = PL (Run @('add-ref-plane','--source','front','--distance','15'))
    Run @('start-sketch','--plane',$rp) | Out-Null
    Run @('sketch-line','--x1','8','--y1','28','--x2','28','--y2','8') | Out-Null
    $rs = SK (Run @('end-sketch'))

    $rib = Run @('rib','--sketch',$rs,'--thickness','6')
    Check "rib returns success message" ($rib.message -match 'rib') $rib.message

    Run @('save-part','--out',$ribPart) | Out-Null
    $i = (Run @('inspect-part','--input',$ribPart)).data
    Check "rib: 1 solid body" ($i.bodyCount -eq 1) "got $($i.bodyCount)"
    Check "rib: bbox 40x40x30 (rib is internal)" (($i.sizeMm.x -eq 40) -and ($i.sizeMm.y -eq 40) -and ($i.sizeMm.z -eq 30)) "got $($i.sizeMm.x)x$($i.sizeMm.y)x$($i.sizeMm.z)"
    Check "rib: 11 faces (L-bracket 8 + gusset 3)" ($i.totalFaceCount -eq 11) "got $($i.totalFaceCount)"
    Check "rib: 27 edges (L-bracket 18 + gusset 9)" ($i.totalEdgeCount -eq 27) "got $($i.totalEdgeCount)"
    $hasRib = @($i.features | Where-Object { $_.typeName -match 'Rib' }).Count -ge 1
    Check "rib: a Rib-type feature is present" $hasRib "features: $(($i.features | ForEach-Object { $_.typeName }) -join ',')"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M35 rib -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    if (Test-Path $ribPart) { Remove-Item $ribPart -Force -ErrorAction SilentlyContinue }
}
