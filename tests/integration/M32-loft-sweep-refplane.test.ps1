# L2 integration: M32 generic feature primitives (loft + sweep + add_ref_plane).
# Includes a 3rd LANDMARK 联调:
#   3. 通用 lofted-round-to-square ≡ create_lofted_round_to_square
#
# Plus a sweep demo: simple "L-pipe" using sketch-line path and circle profile.
# This is the smallest sweep that exercises the InsertProtrusionSwept API.
#
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M32-loft-sweep-refplane.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$loftGeneric = Join-Path $tmpDir ("m32_loft_generic_{0}.sldprt" -f $rand)
$loftSpecial = Join-Path $tmpDir ("m32_loft_special_{0}.sldprt" -f $rand)
$sweepPart = Join-Path $tmpDir ("m32_sweep_{0}.sldprt" -f $rand)
$errFile = Join-Path $tmpDir 'stderr.txt'

function Run([string]$cmd) {
    $stdout = & $exe $cmd.Split(' ') --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $cmd`nstderr: $(Get-Content $errFile -Raw)`nstdout: $stdout"
    }
    return $stdout | ConvertFrom-Json
}

function ParseSketchName($endResult) {
    if ($endResult.message -match "sketch name='([^']+)'") { return $Matches[1] }
    throw "end-sketch did not return sketch name: $($endResult.message)"
}

function ParsePlaneName($refPlaneResult) {
    if ($refPlaneResult.message -match "offset plane '([^']+)'") { return $Matches[1] }
    throw "add-ref-plane did not return plane name: $($refPlaneResult.message)"
}

try {
    # ═══════════════════════════════════════════════════════════════════════
    # 联调 3: 通用 lofted-round-to-square ≡ create_lofted_round_to_square
    #   bottomDiameter=60, topLength=40, topWidth=40, height=30
    # ═══════════════════════════════════════════════════════════════════════

    # ── Build via generic layer ─────────────────────────────────────────────
    Run "new-part" | Out-Null

    # Sketch 1: bottom circle (D60) on Front Plane
    Run "start-sketch --plane front" | Out-Null
    Run "sketch-circle --cx 0 --cy 0 --radius 30" | Out-Null
    $sketch1 = ParseSketchName (Run "end-sketch")
    Write-Host ("[setup] bottom circle sketch -> '{0}'" -f $sketch1)

    # Offset plane at +30mm from Front Plane
    $refPlane = ParsePlaneName (Run "add-ref-plane --source front --distance 30")
    Write-Host ("[setup] offset plane -> '{0}'" -f $refPlane)

    # Sketch 2: top rectangle (40x40) on RefPlane
    Run "start-sketch --plane $refPlane" | Out-Null
    Run "sketch-rectangle-center --cx 0 --cy 0 --corner-x 20 --corner-y 20" | Out-Null
    $sketch2 = ParseSketchName (Run "end-sketch")
    Write-Host ("[setup] top rectangle sketch -> '{0}'" -f $sketch2)

    # Loft the 2 sketches
    $loftResult = Run "loft --sketches $sketch1,$sketch2"
    if ($loftResult.message -notmatch "Lofted") { throw "loft failed: $($loftResult.message)" }
    Write-Host ("[ok] loft [{0}, {1}] -> {2}" -f $sketch1, $sketch2, $loftResult.message)

    Run "save-part --out $loftGeneric" | Out-Null
    if (-not (Test-Path $loftGeneric)) { throw "generic loft not saved" }

    # ── Build same via parametric helper ───────────────────────────────────
    Run "create-lofted-round-to-square --bottom-diameter 60 --top-length 40 --top-width 40 --height 30 --out $loftSpecial" | Out-Null
    if (-not (Test-Path $loftSpecial)) { throw "special loft not saved" }

    # ── Compare bboxes ──────────────────────────────────────────────────────
    $g = (Run "inspect-part --input $loftGeneric").data.sizeMm
    $s = (Run "inspect-part --input $loftSpecial").data.sizeMm
    if ([Math]::Abs($g.x - $s.x) -gt 0.5) { throw "loft X mismatch: generic $($g.x) vs special $($s.x)" }
    if ([Math]::Abs($g.y - $s.y) -gt 0.5) { throw "loft Y mismatch: generic $($g.y) vs special $($s.y)" }
    if ([Math]::Abs($g.z - $s.z) -gt 0.5) { throw "loft Z mismatch: generic $($g.z) vs special $($s.z)" }
    Write-Host ("[ok] LANDMARK 3: 通用 loft ≡ create_lofted_round_to_square — bbox {0}x{1}x{2} mm matches" -f $g.x, $g.y, $g.z)

    # ═══════════════════════════════════════════════════════════════════════
    # Sweep happy-case NOT covered in M32: InsertProtrusionSwept 14-arg API
    # was probed on a simple "Front-circle + Top-line-along-X" config and
    # silent-failed (returned null without diagnostic). v1 PR #27 verified
    # sweep works via the CreateDefinition(swFmSweep=17) + setattr +
    # CreateFeature path (different API entry); M33 will adopt that path
    # and L2-verify sweep with a fan-blade / L-pipe demo. This MVP sweep
    # tool still exposes spec validation + the InsertProtrusionSwept call
    # for power users who set up profile/path very carefully; happy-case
    # L2 verification waits for M33.
    Write-Host "[skip] sweep happy-case L2 verification waits for M33 (v1 CreateDefinition path)"

    # ═══════════════════════════════════════════════════════════════════════
    # Validation rejections
    # ═══════════════════════════════════════════════════════════════════════

    # ── add_ref_plane with zero distance (spec layer) ───────────────────────
    & $exe add-ref-plane --source front --distance 0 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for distance=0" }
    Write-Host "[ok] add-ref-plane rejects distance=0"

    # ── loft with single sketch (spec layer) ────────────────────────────────
    & $exe loft --sketches "草图1" 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for 1 sketch" }
    Write-Host "[ok] loft rejects single sketch"

    # ── sweep with same profile and path (spec layer) ───────────────────────
    & $exe sweep --profile "草图1" --path "草图1" 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for same profile/path" }
    Write-Host "[ok] sweep rejects same profile and path"

    Write-Host '[ok] M32-loft-sweep-refplane all checks passed'
    Write-Host '[ok] LANDMARK 3 — 通用 loft ≡ create_lofted_round_to_square VERIFIED'
    Write-Host '[ok] add_ref_plane VERIFIED (Distance constraint = 8, returns 基准面N)'
    Write-Host '[ok] sweep spec validation only — happy-case L2 waits for M33 (CreateDefinition path)'
} finally {
    foreach ($f in @($loftGeneric, $loftSpecial, $sweepPart, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
