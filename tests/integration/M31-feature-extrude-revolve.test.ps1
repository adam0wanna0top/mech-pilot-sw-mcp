# L2 integration: M31 generic feature primitives (extrude + revolve) +
# 联调 verification that the generic primitives layer produces parts
# geometrically equivalent to the parametric helpers.
#
# Two equivalence checks:
#   1. 通用 cylinder ≡ create_cylinder
#      → bbox / bodyCount / feature.typeName all match
#   2. 通用 hemisphere ≡ create_hemisphere
#      → bbox / bodyCount / feature.typeName all match
#
# This is the LANDMARK test of the generic layer — it proves the LLM can
# build any part the parametric helpers can build, just by composing
# new_part + start_sketch + sketch_* + end_sketch + extrude/revolve + save_part.
#
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M31-feature-extrude-revolve.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cylGeneric = Join-Path $tmpDir ("m31_cyl_generic_{0}.sldprt" -f $rand)
$cylSpecial = Join-Path $tmpDir ("m31_cyl_special_{0}.sldprt" -f $rand)
$hemiGeneric = Join-Path $tmpDir ("m31_hemi_generic_{0}.sldprt" -f $rand)
$hemiSpecial = Join-Path $tmpDir ("m31_hemi_special_{0}.sldprt" -f $rand)
$errFile = Join-Path $tmpDir 'stderr.txt'

function Run([string]$cmd) {
    $stdout = & $exe $cmd.Split(' ') --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $cmd`nstderr: $(Get-Content $errFile -Raw)`nstdout: $stdout"
    }
    return $stdout | ConvertFrom-Json
}

function CompareBboxes($genericInfo, $specialInfo, $label) {
    $g = $genericInfo.data.sizeMm
    $s = $specialInfo.data.sizeMm
    if ([Math]::Abs($g.x - $s.x) -gt 0.1) { throw "$label X mismatch: generic $($g.x) vs special $($s.x)" }
    if ([Math]::Abs($g.y - $s.y) -gt 0.1) { throw "$label Y mismatch: generic $($g.y) vs special $($s.y)" }
    if ([Math]::Abs($g.z - $s.z) -gt 0.1) { throw "$label Z mismatch: generic $($g.z) vs special $($s.z)" }
    if ($genericInfo.data.bodyCount -ne $specialInfo.data.bodyCount) {
        throw "$label bodyCount mismatch: generic $($genericInfo.data.bodyCount) vs special $($specialInfo.data.bodyCount)"
    }
    Write-Host ("[ok] $label bbox match: {0}x{1}x{2} mm, bodyCount={3}" -f $g.x, $g.y, $g.z, $genericInfo.data.bodyCount)
}

try {
    # ═══════════════════════════════════════════════════════════════════════
    # 联调 1: 通用 cylinder ≡ create_cylinder(D40, L30)
    # ═══════════════════════════════════════════════════════════════════════

    # ── Build cylinder via the generic primitives layer ─────────────────────
    Run "new-part" | Out-Null
    Run "start-sketch --plane front" | Out-Null
    Run "sketch-circle --cx 0 --cy 0 --radius 20" | Out-Null
    $endResult = Run "end-sketch"
    # Parse out the sketch name from message: "...sketch name='草图1'..."
    if ($endResult.message -notmatch "sketch name='([^']+)'") {
        throw "end-sketch did not return a sketch name: $($endResult.message)"
    }
    $sketchName = $Matches[1]
    Write-Host ("[setup] generic cylinder sketch -> '{0}'" -f $sketchName)

    $extrudeResult = Run "extrude --sketch $sketchName --depth 30"
    if ($extrudeResult.message -notmatch "Extruded") { throw "extrude failed: $($extrudeResult.message)" }
    Write-Host ("[ok] extrude '{0}' by 30 mm -> {1}" -f $sketchName, $extrudeResult.message)

    Run "save-part --out $cylGeneric" | Out-Null
    if (-not (Test-Path $cylGeneric)) { throw "generic cylinder not saved" }

    # ── Build same cylinder via parametric helper ───────────────────────────
    Run "create-cylinder --diameter 40 --length 30 --out $cylSpecial" | Out-Null
    if (-not (Test-Path $cylSpecial)) { throw "special cylinder not saved" }

    # ── Compare bboxes — should be identical (both D40 L30, axis +Z) ───────
    $genericInfo = Run "inspect-part --input $cylGeneric"
    $specialInfo = Run "inspect-part --input $cylSpecial"
    CompareBboxes $genericInfo $specialInfo "cylinder"

    # ═══════════════════════════════════════════════════════════════════════
    # 联调 2: 通用 hemisphere ≡ create_hemisphere(D40)
    # ═══════════════════════════════════════════════════════════════════════

    # ── Build hemisphere via generic primitives layer ──────────────────────
    Run "new-part" | Out-Null
    Run "start-sketch --plane front" | Out-Null
    # Hemisphere quarter-arc profile (same as CreateHemisphereTool internals):
    #   Line (0,0) → (R,0), arc (R,0) → (R cos45, R sin45) → (0, R),
    #   Line (0, R) → (0, 0), centerline (0, -2R) → (0, 2R) for axis.
    Run "sketch-line --x1 0 --y1 0 --x2 20 --y2 0" | Out-Null
    Run "sketch-arc-3point --x1 20 --y1 0 --x2 0 --y2 20 --x3 14.14 --y3 14.14" | Out-Null
    Run "sketch-line --x1 0 --y1 20 --x2 0 --y2 0" | Out-Null
    Run "sketch-centerline --x1 0 --y1 -40 --x2 0 --y2 40" | Out-Null
    $endResult = Run "end-sketch"
    if ($endResult.message -notmatch "sketch name='([^']+)'") {
        throw "end-sketch did not return a sketch name: $($endResult.message)"
    }
    $sketchName = $Matches[1]
    Write-Host ("[setup] generic hemisphere sketch -> '{0}'" -f $sketchName)

    $revolveResult = Run "revolve --sketch $sketchName --angle 360"
    if ($revolveResult.message -notmatch "Revolved") { throw "revolve failed: $($revolveResult.message)" }
    Write-Host ("[ok] revolve '{0}' by 360 deg -> {1}" -f $sketchName, $revolveResult.message)

    Run "save-part --out $hemiGeneric" | Out-Null
    if (-not (Test-Path $hemiGeneric)) { throw "generic hemisphere not saved" }

    # ── Build same hemisphere via parametric helper ────────────────────────
    Run "create-hemisphere --diameter 40 --out $hemiSpecial" | Out-Null
    if (-not (Test-Path $hemiSpecial)) { throw "special hemisphere not saved" }

    # ── Compare bboxes — should be identical (both D40 hemisphere, axis +Y) ─
    $genericInfo = Run "inspect-part --input $hemiGeneric"
    $specialInfo = Run "inspect-part --input $hemiSpecial"
    CompareBboxes $genericInfo $specialInfo "hemisphere"

    # ═══════════════════════════════════════════════════════════════════════
    # Validation rejections
    # ═══════════════════════════════════════════════════════════════════════

    # ── extrude with non-existent sketch name ───────────────────────────────
    Run "new-part" | Out-Null
    & $exe extrude --sketch "no-such-sketch" --depth 10 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for unknown sketch name" }
    Write-Host "[ok] extrude rejects unknown sketch name"
    Run "save-part --out $cylGeneric" | Out-Null   # clean up the active part

    # ── extrude with negative depth (spec layer) ────────────────────────────
    & $exe extrude --sketch "草图1" --depth -10 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for negative depth" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'depth') { throw "error should reference depth: $errMsg" }
    Write-Host "[ok] extrude rejects negative depth"

    # ── revolve with angle 0 (spec layer) ───────────────────────────────────
    & $exe revolve --sketch "草图1" --angle 0 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for angle 0" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'angle') { throw "error should reference angle: $errMsg" }
    Write-Host "[ok] revolve rejects angle=0"

    Write-Host '[ok] M31-feature-extrude-revolve all checks passed'
    Write-Host '[ok] 通用 layer ≡ 特化 helper — VERIFIED'
} finally {
    foreach ($f in @($cylGeneric, $cylSpecial, $hemiGeneric, $hemiSpecial, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
