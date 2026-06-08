# L2 integration: M34 — extrude_cut + revolve_cut HAPPY cases (LANDMARK 4 cuts).
#
# Root-cause corrected vs M33: the cut "failures" were NOT face-based-vs-plane-
# based or selection-state — they were geometry/test-setup. The cut sketch must
# sit on a plane/face that BOUNDS the body where the cut enters (extrude_cut),
# and the revolve profile must overlap the body + carry a centerline axis
# (revolve_cut). With correct geometry both work via the generic plane-based layer.
#
#   Test 1 extrude_cut: cylinder D40x30 + 10x10 square THROUGH hole
#       (ref plane at the top face, cut back down). Verify 7 faces / 14 edges.
#   Test 2 revolve_cut: cylinder D40x30 (revolve about Y) + circumferential V
#       groove (revolve_cut 360). Verify 6 faces / 5 edges, bbox 40x30x40.
#   Test 3 extrude_cut on the body's BASE plane now cuts into the body — the old
#       "base plane won't cut" limit was a symptom of the reverse->Flip mis-wiring
#       (both tries were Dir:false); Dir-based auto-detect now cuts the same hole.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M34-cut-happy.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$extrudeCutPart = Join-Path $tmpDir ("m34_extrude_cut_{0}.sldprt" -f $rand)
$revolveCutPart = Join-Path $tmpDir ("m34_revolve_cut_{0}.sldprt" -f $rand)
$basePlaneCutPart = Join-Path $tmpDir ("m34_baseplane_cut_{0}.sldprt" -f $rand)

$script:fail = 0
function Check([string]$label, [bool]$cond, [string]$detail = '') {
    if ($cond) { Write-Host "[ok] $label" }
    else { Write-Host "[FAIL] $label $detail"; $script:fail++ }
}

# Throws on non-zero exit (for setup steps that must succeed).
function Run([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    $raw = ($o -join "`n")
    if ($LASTEXITCODE -ne 0) { throw "command failed: $($a -join ' ')`n$raw" }
    $obj = $null; try { $obj = $raw | ConvertFrom-Json } catch {}
    return $obj
}
# Never throws; returns Code + Out (for the negative case).
function TryRun([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($o -join "`n") }
}
function SK($obj) { if ($obj.message -match "sketch name='([^']+)'") { return $Matches[1] } ; throw "no sketch name: $($obj.message)" }
function PL($obj) { if ($obj.message -match "plane '([^']+)'") { return $Matches[1] } ; throw "no plane name: $($obj.message)" }

try {
    # ═══════════════════════════════════════════════════════════════════════
    # Test 1 — extrude_cut: square through hole in a cylinder
    # ═══════════════════════════════════════════════════════════════════════
    Write-Host "== Test 1: extrude_cut square-through-hole =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $c1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$c1,'--depth','30') | Out-Null
    $rp = PL (Run @('add-ref-plane','--source','front','--distance','30'))
    Run @('start-sketch','--plane',$rp) | Out-Null
    Run @('sketch-rectangle-center','--cx','0','--cy','0','--corner-x','5','--corner-y','5') | Out-Null
    $c2 = SK (Run @('end-sketch'))
    $cut = Run @('extrude-cut','--sketch',$c2,'--depth','50')   # 50 > 30 => through
    Check "extrude_cut returns a feature" ($cut.message -match 'feature') $cut.message
    Run @('save-part','--out',$extrudeCutPart) | Out-Null
    $i1 = (Run @('inspect-part','--input',$extrudeCutPart)).data
    Check "extrude_cut: 1 solid body" ($i1.bodyCount -eq 1) "got $($i1.bodyCount)"
    Check "extrude_cut: bbox 40x40x30" (($i1.sizeMm.x -eq 40) -and ($i1.sizeMm.y -eq 40) -and ($i1.sizeMm.z -eq 30)) "got $($i1.sizeMm.x)x$($i1.sizeMm.y)x$($i1.sizeMm.z)"
    Check "extrude_cut: 7 faces (3 cyl + 4 square walls)" ($i1.totalFaceCount -eq 7) "got $($i1.totalFaceCount)"
    Check "extrude_cut: 14 edges" ($i1.totalEdgeCount -eq 14) "got $($i1.totalEdgeCount)"

    # ═══════════════════════════════════════════════════════════════════════
    # Test 2 — revolve_cut: circumferential V groove in a cylinder (axis Y)
    # ═══════════════════════════════════════════════════════════════════════
    Write-Host "== Test 2: revolve_cut V-groove =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-line','--x1','0','--y1','0','--x2','20','--y2','0') | Out-Null
    Run @('sketch-line','--x1','20','--y1','0','--x2','20','--y2','30') | Out-Null
    Run @('sketch-line','--x1','20','--y1','30','--x2','0','--y2','30') | Out-Null
    Run @('sketch-line','--x1','0','--y1','30','--x2','0','--y2','0') | Out-Null
    Run @('sketch-centerline','--x1','0','--y1','0','--x2','0','--y2','30') | Out-Null
    $r1 = SK (Run @('end-sketch'))
    Run @('revolve','--sketch',$r1,'--angle','360') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-line','--x1','20','--y1','13','--x2','20','--y2','17') | Out-Null
    Run @('sketch-line','--x1','20','--y1','17','--x2','17','--y2','15') | Out-Null
    Run @('sketch-line','--x1','17','--y1','15','--x2','20','--y2','13') | Out-Null
    Run @('sketch-centerline','--x1','0','--y1','0','--x2','0','--y2','30') | Out-Null
    $r2 = SK (Run @('end-sketch'))
    $rcut = Run @('revolve-cut','--sketch',$r2,'--angle','360')
    Check "revolve_cut returns a feature" ($rcut.message -match 'feature') $rcut.message
    Run @('save-part','--out',$revolveCutPart) | Out-Null
    $i2 = (Run @('inspect-part','--input',$revolveCutPart)).data
    Check "revolve_cut: 1 solid body" ($i2.bodyCount -eq 1) "got $($i2.bodyCount)"
    Check "revolve_cut: bbox 40x30x40" (($i2.sizeMm.x -eq 40) -and ($i2.sizeMm.y -eq 30) -and ($i2.sizeMm.z -eq 40)) "got $($i2.sizeMm.x)x$($i2.sizeMm.y)x$($i2.sizeMm.z)"
    Check "revolve_cut: 6 faces (V groove splits side)" ($i2.totalFaceCount -eq 6) "got $($i2.totalFaceCount)"
    Check "revolve_cut: 5 edges" ($i2.totalEdgeCount -eq 5) "got $($i2.totalEdgeCount)"

    # ═══════════════════════════════════════════════════════════════════════
    # Test 3 — base-plane cut now cuts INTO the body (fixed: Dir-based auto-detect)
    #   Pre-fix this was rejected ("base plane won't cut, in any direction"). That
    #   was a symptom of the reverse→Flip mis-wiring: both tries were Dir:false
    #   (anti-normal = away from the body). With reverse wired to Dir, the
    #   auto-detect's 2nd try (Dir:true) cuts +Z into the body, so a base-plane
    #   sketch now makes the SAME square through hole as the ref-plane cut (Test 1).
    # ═══════════════════════════════════════════════════════════════════════
    Write-Host "== Test 3: base-plane cut now cuts into the body =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $b1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$b1,'--depth','30') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-rectangle-center','--cx','0','--cy','0','--corner-x','5','--corner-y','5') | Out-Null
    $b2 = SK (Run @('end-sketch'))
    $bcut = Run @('extrude-cut','--sketch',$b2,'--depth','50')   # base-plane sketch; auto-detect cuts +Z through the body
    Check "base-plane cut returns a feature" ($bcut.message -match 'feature') $bcut.message
    Run @('save-part','--out',$basePlaneCutPart) | Out-Null
    $i3 = (Run @('inspect-part','--input',$basePlaneCutPart)).data
    Check "base-plane cut: 1 solid body" ($i3.bodyCount -eq 1) "got $($i3.bodyCount)"
    Check "base-plane cut: bbox 40x40x30" (($i3.sizeMm.x -eq 40) -and ($i3.sizeMm.y -eq 40) -and ($i3.sizeMm.z -eq 30)) "got $($i3.sizeMm.x)x$($i3.sizeMm.y)x$($i3.sizeMm.z)"
    Check "base-plane cut: 7 faces (same through-hole as the ref-plane cut)" ($i3.totalFaceCount -eq 7) "got $($i3.totalFaceCount)"
    Check "base-plane cut: 14 edges" ($i3.totalEdgeCount -eq 14) "got $($i3.totalEdgeCount)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M34 cut happy cases -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($extrudeCutPart, $revolveCutPart, $basePlaneCutPart)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
