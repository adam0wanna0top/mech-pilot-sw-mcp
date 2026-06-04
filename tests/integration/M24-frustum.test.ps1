# L2 integration: create-frustum should produce a solid truncated cone
# (4-line trapezoid profile + Y-axis centerline + 360° revolve) and save as
# .sldprt. M24 — second revolved-geometry tool, reuses the same sketch+revolve
# framework as M23 create_hemisphere but the profile is a trapezoid instead
# of a quarter-circle.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M24-frustum.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$frustum   = Join-Path $tmpDir ("frustum_{0}.sldprt"      -f (Get-Random))
$nearCone  = Join-Path $tmpDir ("frustum_cone_{0}.sldprt" -f (Get-Random))
$errFile   = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy: baseD 60 / topD 30 / height 40 → solid frustum, axis +Y ──────
    #   Expected bbox X×Y×Z = 60 × 40 × 60 (Y ∈ [0, 40], X/Z ∈ [-30, 30]).
    $stdout = & $exe create-frustum --base-diameter 60 --top-diameter 30 --height 40 --out $frustum --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-frustum (60/30/40) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')              { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $frustum)            { throw "json path mismatch: '$($r.path)' vs '$frustum'" }
    if (-not (Test-Path $frustum))       { throw ".sldprt not created: $frustum" }
    Write-Host ("[ok] frustum baseD60 topD30 H40 -> {0} ({1:N0} bytes)" -f $frustum, (Get-Item $frustum).Length)

    # ── geometry verification via inspect-part ──────────────────────────────
    #   featureCount = 2 (sketch + Revolution); bbox = 60 × 40 × 60.
    #   M23 收尾 established geometry-not-just-API-ok L2 verification for
    #   revolved geometry; same pattern here.
    $stdout = & $exe inspect-part --input $frustum --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-part failed: $(Get-Content $errFile -Raw)" }
    $info = $stdout | ConvertFrom-Json
    if ($info.data.featureCount -ne 2)   { throw "featureCount expected 2 (sketch + revolve), got $($info.data.featureCount)" }
    $size = $info.data.sizeMm
    if ([Math]::Abs($size.x - 60) -gt 0.01) { throw "size.x expected 60, got $($size.x)" }
    if ([Math]::Abs($size.y - 40) -gt 0.01) { throw "size.y expected 40 (=height), got $($size.y)" }
    if ([Math]::Abs($size.z - 60) -gt 0.01) { throw "size.z expected 60, got $($size.z)" }
    $hasRevolve = $false
    foreach ($f in $info.data.features) {
        if ($f.typeName -eq 'Revolution') { $hasRevolve = $true; break }
    }
    if (-not $hasRevolve) { throw "Revolution feature not found in: $($info.data.features.typeName -join ', ')" }
    Write-Host ("[ok] geometry verified: bbox {0}x{1}x{2} mm + Revolution feature" -f $size.x, $size.y, $size.z)

    # ── happy: robot-joint taper (baseD40 topD20 H15) ───────────────────────
    #   Typical mech-arm joint taper proportions. NOTE: SW sketch precision
    #   limits the smallest topDiameter that still produces a valid top-radius
    #   line — empirical M24 L2 finding: topD=1 mm (topR=0.5 mm line) makes
    #   ISketchManager.CreateLine return null. LLM should use topD >= 2-3 mm
    #   for safety; true cones (topD=0) await a future create_cone tool.
    $stdout = & $exe create-frustum --base-diameter 40 --top-diameter 20 --height 15 --out $nearCone --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-frustum (robot taper 40/20/15) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    if (-not (Test-Path $nearCone)) { throw "robot-taper frustum not created" }
    Write-Host ("[ok] robot-taper baseD40 topD20 H15 -> {0} ({1:N0} bytes)" -f $nearCone, (Get-Item $nearCone).Length)

    # ── validation: top >= base (spec layer) → cylinder hint ────────────────
    & $exe create-frustum --base-diameter 30 --top-diameter 30 --height 20 --out $frustum 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for topD == baseD" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'create_cylinder') { throw "error should suggest create_cylinder: $errMsg" }
    Write-Host "[ok] validation rejects top == base, hints to create_cylinder"

    # ── validation: top > base (inverted) ───────────────────────────────────
    & $exe create-frustum --base-diameter 30 --top-diameter 60 --height 20 --out $frustum 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for inverted frustum" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'strictly less than') { throw "error should reference 'strictly less than': $errMsg" }
    Write-Host "[ok] validation rejects inverted frustum"

    # ── validation: negative height (spec layer) ────────────────────────────
    & $exe create-frustum --base-diameter 60 --top-diameter 30 --height -5 --out $frustum 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for negative height" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'height')      { throw "error should reference height: $errMsg" }
    Write-Host "[ok] validation rejects negative height"

    Write-Host '[ok] M24-frustum all checks passed'
} finally {
    foreach ($f in @($frustum, $nearCone, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
