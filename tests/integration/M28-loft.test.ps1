# L2 integration: create-lofted-round-to-square should produce a solid
# transition between a round bottom (circle) and rectangular top via
# SW's InsertProtrusionBlend. M28 — first multi-plane sketch tool in the
# project (Sketch1 on Front Plane + Sketch2 on auto-created RefPlane1).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M28-loft.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$loft1 = Join-Path $tmpDir ("loft_60_40_30_{0}.sldprt" -f (Get-Random))
$loft2 = Join-Path $tmpDir ("loft_asym_{0}.sldprt"     -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy: D60 → 40×40 × H30 (HVAC 风管接头) ────────────────────────────
    #   Expected: Z extent = 30, X extent = max(D60, L40) = 60,
    #   Y extent = max(D60, W40) = 60. The loft body's bbox conservatively
    #   encloses both profiles.
    $stdout = & $exe create-lofted-round-to-square --bottom-diameter 60 --top-length 40 --top-width 40 --height 30 --out $loft1 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-lofted-round-to-square (D60→40×40×H30) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $loft1)             { throw "json path mismatch: '$($r.path)' vs '$loft1'" }
    if (-not (Test-Path $loft1))        { throw ".sldprt not created: $loft1" }
    Write-Host ("[ok] D60 → 40x40 H30 lofted transition -> {0} ({1:N0} bytes)" -f $loft1, (Get-Item $loft1).Length)

    # ── geometry verification: feature list should include Blend ────────────
    #   inspect_part filters out RefPlane (boot-feature type), so the user-
    #   meaningful feature list is 2 ProfileFeatures (sketches) + 1 Blend
    #   (SW's internal typeName for loft via InsertProtrusionBlend).
    $stdout = & $exe inspect-part --input $loft1 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-part failed: $(Get-Content $errFile -Raw)" }
    $info = $stdout | ConvertFrom-Json
    if ($info.data.featureCount -lt 3) {
        throw "featureCount expected >= 3 (sketch1 + sketch2 + Blend), got $($info.data.featureCount): $($info.data.features.typeName -join ', ')"
    }
    $size = $info.data.sizeMm
    # Z extent = height (loft direction) — strictest assertion.
    if ([Math]::Abs($size.z - 30) -gt 0.5) { throw "size.z expected 30 (=height), got $($size.z)" }
    # X / Y extents = max(bottomD, topL/W). For D60 → 40×40, both equal 60.
    if ([Math]::Abs($size.x - 60) -gt 0.5) { throw "size.x expected 60 (=max(D60, L40)), got $($size.x)" }
    if ([Math]::Abs($size.y - 60) -gt 0.5) { throw "size.y expected 60 (=max(D60, W40)), got $($size.y)" }
    # Loft feature must be present — SW internal typeName is "Blend" via
    # InsertProtrusionBlend (verified at L2 first run).
    $hasLoft = $false
    foreach ($f in $info.data.features) {
        if ($f.typeName -match 'Loft|Blend|Protrusion') { $hasLoft = $true; break }
    }
    if (-not $hasLoft) {
        throw "Blend / Loft feature not found in: $($info.data.features.typeName -join ', ')"
    }
    Write-Host ("[ok] geometry verified: bbox {0}x{1}x{2} mm (Z=H=30, X/Y=max=60), featureCount={3} (Blend feature confirmed)" -f $size.x, $size.y, $size.z, $info.data.featureCount)

    # ── happy: asymmetric L != W rectangular top (D40 → 80×20 × H25) ────────
    #   Asymmetric top tests that L and W are handled independently.
    $stdout = & $exe create-lofted-round-to-square --bottom-diameter 40 --top-length 80 --top-width 20 --height 25 --out $loft2 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-lofted-round-to-square (asym) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    if (-not (Test-Path $loft2)) { throw "asym loft not created" }
    Write-Host ("[ok] D40 → 80x20 H25 asymmetric lofted transition -> {0} ({1:N0} bytes)" -f $loft2, (Get-Item $loft2).Length)

    # ── validation: negative dimension (spec layer) ─────────────────────────
    & $exe create-lofted-round-to-square --bottom-diameter -10 --top-length 40 --top-width 40 --height 30 --out $loft1 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for negative bottom-diameter" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'bottomDiameter') { throw "error should reference bottomDiameter: $errMsg" }
    Write-Host "[ok] validation rejects negative bottom-diameter"

    # ── validation: missing parent directory (spec layer) ───────────────────
    $badPath = Join-Path $tmpDir ("no-such-{0}\loft.sldprt" -f (Get-Random))
    & $exe create-lofted-round-to-square --bottom-diameter 60 --top-length 40 --top-width 40 --height 30 --out $badPath 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for missing parent" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'parent directory') { throw "error should reference parent directory: $errMsg" }
    Write-Host "[ok] validation rejects missing parent directory"

    Write-Host '[ok] M28-loft all checks passed'
} finally {
    foreach ($f in @($loft1, $loft2, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
