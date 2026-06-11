# L2 integration: M50 — curve enhancement (sketch_spline + insert_helix +
# sweep path accepting curve features).
#
#   Test A  spline: free-form wave profile (spline through 3 points + closing
#           line) extruded to a solid — bbox proves the spline bulge.
#   Test B  SPRING (the M50 landmark): front-plane circle Ø30 → insert_helix
#           pitch 8 × 5 rev → top-plane wire profile Ø4 at (15, 0) → sweep
#           along the helix (REFERENCECURVES path fallback). bbox ≈ 34×34×~44.
#   Test C  negatives: 2-point spline rejected with sketch_line hint;
#           insert_helix without an active sketch rejected.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M50-curves.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$wave = Join-Path $tmpDir ("m50_wave_{0}.sldprt" -f $rand)
$spring = Join-Path $tmpDir ("m50_spring_{0}.sldprt" -f $rand)

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
function TryRun([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($o -join "`n") }
}
function SK($obj) { if ($obj.message -match "sketch name='([^']+)'") { return $Matches[1] }; throw "no sketch name: $($obj.message)" }

try {
    # ═══ A. Spline wave block ═══════════════════════════════════════════════
    Write-Host "== A: spline wave profile -> extrude =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    $sp = Run @('sketch-spline','--points','0','0','15','8','30','0')
    Check "spline reports 3 points" ($sp.message -match 'through 3 points') $sp.message
    Run @('sketch-line','--x1','30','--y1','0','--x2','0','--y2','0') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s1,'--depth','10') | Out-Null
    $a1 = (Run @('inspect-active')).data
    Check "wave block: bbox x=30, z=10" `
        (([Math]::Abs($a1.sizeMm.x - 30) -lt 0.2) -and ([Math]::Abs($a1.sizeMm.z - 10) -lt 0.1)) `
        "got $($a1.sizeMm.x)x$($a1.sizeMm.y)x$($a1.sizeMm.z)"
    # Natural-end splines overshoot between points (observed y=12 for an 8 mm
    # peak) — the curve still passes through every input point. Assert the
    # bulge exists and is in the natural-ends envelope.
    Check "wave block: spline bulge y in [8, 13]" `
        (($a1.sizeMm.y -ge 8) -and ($a1.sizeMm.y -le 13)) "y=$($a1.sizeMm.y)"
    Run @('save-part','--out',$wave) | Out-Null

    # ═══ B. SPRING: circle -> helix -> sweep ════════════════════════════════
    Write-Host "== B: spring (helix path sweep) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','15') | Out-Null
    # NO end-sketch — insert-helix consumes the active sketch.
    $hx = Run @('insert-helix','--pitch','8','--revolutions','5')
    Check "helix created with height 40" ($hx.message -match 'height 40 mm') $hx.message
    if ($hx.message -notmatch "helix '([^']+)'") { throw "no helix name in: $($hx.message)" }
    $helixName = $Matches[1]
    Write-Host ("[info] helix feature = '{0}'" -f $helixName)

    Run @('start-sketch','--plane','top') | Out-Null
    Run @('sketch-circle','--cx','15','--cy','0','--radius','2') | Out-Null
    $prof = SK (Run @('end-sketch'))
    $sw = Run @('sweep','--profile',$prof,'--path',$helixName)
    Check "sweep along helix succeeds" ($sw.message -match 'Swept') $sw.message
    $b1 = (Run @('inspect-active')).data
    Check "spring: 1 body" ($b1.bodyCount -eq 1) "got $($b1.bodyCount)"
    # Observed: SW pierces the wire profile at its EDGE (not center), so the
    # wire center rides at helix R + wire r → envelope = R + 2*wire r = 19
    # → 38 (x exactly 38.0; y slightly larger from the seam/end rotation).
    Check "spring: bbox x,y ~ 38 (helix Ø30, wire edge-pierced)" `
        (($b1.sizeMm.x -ge 33) -and ($b1.sizeMm.x -le 40) -and
         ($b1.sizeMm.y -ge 33) -and ($b1.sizeMm.y -le 40)) `
        "got $($b1.sizeMm.x)x$($b1.sizeMm.y)"
    Check "spring: bbox z ~ 44 (5 rev x pitch 8 + wire)" `
        (($b1.sizeMm.z -ge 40) -and ($b1.sizeMm.z -le 46)) "z=$($b1.sizeMm.z)"
    Run @('save-part','--out',$spring) | Out-Null

    # ═══ C. negatives ═══════════════════════════════════════════════════════
    Write-Host "== C: negatives =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    $bad1 = TryRun @('sketch-spline','--points','0','0','30','0')
    Check "2-point spline exits non-zero" ($bad1.Code -ne 0) "code=$($bad1.Code)"
    Check "2-point spline hints sketch_line" ($bad1.Out -match 'sketch_line') $bad1.Out
    # Helix on a circle-less (still empty) sketch → friendly one-circle guidance.
    $bad2 = TryRun @('insert-helix','--pitch','8','--revolutions','5')
    Check "helix without a circle exits non-zero" ($bad2.Code -ne 0) "code=$($bad2.Code)"
    Check "helix error explains the one-circle contract" ($bad2.Out -match 'ONE circle') $bad2.Out
    # Make the sketch non-empty so end-sketch + save close the doc cleanly.
    Run @('sketch-circle','--cx','0','--cy','0','--radius','5') | Out-Null
    Run @('end-sketch') | Out-Null
    $discard = Join-Path $tmpDir ("m50_discard_{0}.sldprt" -f $rand)
    Run @('save-part','--out',$discard) | Out-Null
    if (Test-Path $discard) { Remove-Item $discard -Force -EA SilentlyContinue }

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M50 curves -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($wave, $spring)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
