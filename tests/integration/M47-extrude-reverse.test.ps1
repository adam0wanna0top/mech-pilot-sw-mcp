# L2 integration: M47 — `extrude --reverse` actually flips the extrude direction.
#
# Regression for the M47 fix. ExtrudeTool wired the `reverse` flag to
# FeatureExtrusion3's `Flip` parameter (a thin-wall flip — a no-op for a solid
# boss) instead of `Dir` (the real reverse-direction flag). Before the fix,
# reverse=true extruded the SAME direction as reverse=false (always +normal),
# which forced every generic-layer build to grow +normal and made downward /
# backward protrusions impossible.
#
# This builds the same circle on the front plane (+Z normal) twice and asserts
# the bbox flips with the flag:
#   reverse=false → z in [  0, +30]   (default, +Z)
#   reverse=true  → z in [-30,   0]   (flipped, -Z)
#
# Requires SolidWorks. Run: powershell -File ./tests/integration/M47-extrude-reverse.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$fwd = Join-Path $tmpDir ("m47_fwd_{0}.sldprt" -f $rand)
$rev = Join-Path $tmpDir ("m47_rev_{0}.sldprt" -f $rand)
$errFile = Join-Path $tmpDir 'stderr_m47.txt'

function Run([string]$cmd) {
    $stdout = & $exe $cmd.Split(' ') --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $cmd`nstderr: $(Get-Content $errFile -Raw)`nstdout: $stdout"
    }
    return $stdout | ConvertFrom-Json
}

# Build a circle on the front plane (+Z normal), extrude 30 mm (optionally
# reversed), and return the live bounding box of the active part.
function BuildAndBox([bool]$reverse) {
    Run "new-part" | Out-Null
    Run "start-sketch --plane front" | Out-Null
    Run "sketch-circle --cx 0 --cy 0 --radius 20" | Out-Null
    $end = Run "end-sketch"
    if ($end.message -notmatch "sketch name='([^']+)'") { throw "no sketch name: $($end.message)" }
    $name = $Matches[1]
    $flag = if ($reverse) { ' --reverse' } else { '' }
    $ex = Run ("extrude --sketch {0} --depth 30{1}" -f $name, $flag)
    if ($ex.message -notmatch 'Extruded') { throw "extrude failed: $($ex.message)" }
    return (Run "inspect-active").data.boundingBoxMm
}

function Near($a, $b) { return [Math]::Abs([double]$a - [double]$b) -lt 0.1 }

try {
    # ── reverse=false → default direction (+Z): z in [0, 30] ────────────────
    $boxF = BuildAndBox $false
    Write-Host ("[info] reverse=false  bbox z = [{0}, {1}]" -f $boxF.minZ, $boxF.maxZ)
    if (-not (Near $boxF.minZ 0))  { throw "fwd minZ expected 0, got $($boxF.minZ)" }
    if (-not (Near $boxF.maxZ 30)) { throw "fwd maxZ expected 30, got $($boxF.maxZ)" }
    Run "save-part --out $fwd" | Out-Null
    Write-Host "[ok] reverse=false extrudes +Z  (z in [0, 30])"

    # ── reverse=true → flipped direction (-Z): z in [-30, 0] ────────────────
    $boxR = BuildAndBox $true
    Write-Host ("[info] reverse=true   bbox z = [{0}, {1}]" -f $boxR.minZ, $boxR.maxZ)
    if (-not (Near $boxR.minZ (-30))) { throw "rev minZ expected -30, got $($boxR.minZ) -- reverse did NOT flip (M47 regression)" }
    if (-not (Near $boxR.maxZ 0))     { throw "rev maxZ expected 0, got $($boxR.maxZ) -- reverse did NOT flip (M47 regression)" }
    Run "save-part --out $rev" | Out-Null
    Write-Host "[ok] reverse=true  extrudes -Z  (z in [-30, 0])"

    # ── the two must be mirror images (the whole point of the fix) ──────────
    if (-not (Near $boxF.maxZ ([double]$boxR.minZ * -1))) { throw "fwd/rev not mirror images" }
    Write-Host "[ok] M47-extrude-reverse: --reverse flips the extrude direction -- VERIFIED"
} finally {
    foreach ($f in @($fwd, $rev, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
