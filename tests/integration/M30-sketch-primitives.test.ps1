# L2 integration: M30 generic sketch primitives.
# Exercises new_part → start_sketch → 6 sketch primitives → end_sketch →
# save_part workflow. Verifies that the generic layer can produce sketches
# equivalent to what parametric helpers produce internally (M31 extrude /
# revolve will close the loop by building real geometry from these sketches).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M30-sketch-primitives.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$partA = Join-Path $tmpDir ("m30_sketches_a_{0}.sldprt" -f $rand)
$errFile = Join-Path $tmpDir 'stderr.txt'

function Run([string]$cmd) {
    $argLine = $cmd
    $stdout = & $exe $argLine.Split(' ') --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $cmd`nstderr: $(Get-Content $errFile -Raw)`nstdout: $stdout"
    }
    return $stdout | ConvertFrom-Json
}

try {
    # ── Sketch 1: D40 circle on Front Plane (like create_cylinder internals) ─
    Run "new-part" | Out-Null
    Run "start-sketch --plane front" | Out-Null
    Run "sketch-circle --cx 0 --cy 0 --radius 20" | Out-Null
    $r = Run "end-sketch"
    if ($r.message -notmatch "sketch name='[^']+'") { throw "end-sketch should return sketch name: $($r.message)" }
    Write-Host ("[ok] Sketch 1: D40 circle on Front Plane -> '{0}'" -f $r.message)

    # ── Sketch 2: rectangle on Top Plane (multi-sketch in same part) ────────
    Run "start-sketch --plane top" | Out-Null
    Run "sketch-rectangle-center --cx 0 --cy 0 --corner-x 30 --corner-y 20" | Out-Null
    Run "end-sketch" | Out-Null
    Write-Host "[ok] Sketch 2: 60x40 rectangle on Top Plane"

    # ── Sketch 3: hemisphere quarter-arc profile on Front Plane (revolve setup) ──
    #   This is the same profile create_hemisphere(D40) builds internally:
    #   1 line + 1 arc + 1 line + 1 centerline. M31 revolve will turn this
    #   into a real half-sphere.
    Run "start-sketch --plane front" | Out-Null
    Run "sketch-line --x1 0 --y1 0 --x2 20 --y2 0" | Out-Null
    Run "sketch-arc-3point --x1 20 --y1 0 --x2 0 --y2 20 --x3 14.14 --y3 14.14" | Out-Null
    Run "sketch-line --x1 0 --y1 20 --x2 0 --y2 0" | Out-Null
    Run "sketch-centerline --x1 0 --y1 -40 --x2 0 --y2 40" | Out-Null
    Run "end-sketch" | Out-Null
    Write-Host "[ok] Sketch 3: hemisphere quarter-arc profile + centerline on Front Plane"

    # ── Sketch 4: center-arc (CCW) on Right Plane (exercises sketch_arc_center) ──
    Run "start-sketch --plane right" | Out-Null
    Run "sketch-arc-center --cx 0 --cy 0 --x1 15 --y1 0 --x2 0 --y2 15 --direction 1" | Out-Null
    Run "end-sketch" | Out-Null
    Write-Host "[ok] Sketch 4: CCW center arc on Right Plane"

    # ── Save the part and verify the 4 sketches survived ────────────────────
    $r = Run "save-part --out $partA"
    if (-not (Test-Path $partA)) { throw "part not saved: $partA" }
    Write-Host ("[ok] save-part wrote .sldprt -> {0} ({1:N0} bytes)" -f $partA, (Get-Item $partA).Length)

    # ── Geometry verification: inspect should report 4 ProfileFeatures ──────
    $info = Run "inspect-part --input $partA"
    if ($info.data.featureCount -ne 4) {
        throw "expected 4 sketches (ProfileFeatures), got $($info.data.featureCount): $($info.data.features.typeName -join ', ')"
    }
    $allProfiles = $true
    foreach ($f in $info.data.features) {
        if ($f.typeName -ne 'ProfileFeature') { $allProfiles = $false; break }
    }
    if (-not $allProfiles) {
        throw "all features should be ProfileFeature (sketches), got: $($info.data.features.typeName -join ', ')"
    }
    if ($info.data.bodyCount -ne 0) {
        throw "no extrude/revolve was done — bodyCount expected 0, got $($info.data.bodyCount)"
    }
    Write-Host ("[ok] inspect: 4 ProfileFeatures, bodyCount=0 (sketches only, no features yet)")

    Write-Host '[ok] M30-sketch-primitives all checks passed'
} finally {
    foreach ($f in @($partA, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
