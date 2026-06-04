# L2 integration: create-sphere should produce a solid sphere (half-disc
# profile + 180° arc + Y-axis centerline + 360° revolve) and save as .sldprt.
# M27 — second revolved-geometry sibling of M23 create_hemisphere, reuses
# the same sketch+revolve framework but the profile is a half-disc instead
# of a quarter-disc. Critical geometry verification: bbox Y must equal D
# (full diameter), distinguishing sphere from hemisphere (which has Y = D/2).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M27-sphere.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$bigSphere = Join-Path $tmpDir ("sphere_big_{0}.sldprt" -f (Get-Random))
$smallSphere = Join-Path $tmpDir ("sphere_small_{0}.sldprt" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy: D40 sphere → solid full sphere centered at origin ────────────
    #   Expected bbox X×Y×Z = 40 × 40 × 40 (all axes ∈ [-20, 20]).
    $stdout = & $exe create-sphere --diameter 40 --out $bigSphere --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-sphere (D40) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')              { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $bigSphere)          { throw "json path mismatch: '$($r.path)' vs '$bigSphere'" }
    if (-not (Test-Path $bigSphere))     { throw ".sldprt not created: $bigSphere" }
    Write-Host ("[ok] D40 sphere -> {0} ({1:N0} bytes)" -f $bigSphere, (Get-Item $bigSphere).Length)

    # ── critical geometry verification: bbox 40×40×40 (Y=D, NOT D/2) ────────
    #   This is THE distinguishing feature vs. hemisphere — hemisphere has
    #   Y = D/2 (only the +Y half), sphere has Y = D (full diameter).
    #   Also checks the Revolution feature actually got applied (M22 收尾 pattern).
    $stdout = & $exe inspect-part --input $bigSphere --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-part failed: $(Get-Content $errFile -Raw)" }
    $info = $stdout | ConvertFrom-Json
    if ($info.data.featureCount -ne 2)   { throw "featureCount expected 2 (sketch + revolve), got $($info.data.featureCount)" }
    $size = $info.data.sizeMm
    if ([Math]::Abs($size.x - 40) -gt 0.01) { throw "size.x expected 40, got $($size.x)" }
    if ([Math]::Abs($size.y - 40) -gt 0.01) { throw "size.y expected 40 (=D for sphere, not D/2 like hemisphere), got $($size.y)" }
    if ([Math]::Abs($size.z - 40) -gt 0.01) { throw "size.z expected 40, got $($size.z)" }
    $hasRevolve = $false
    foreach ($f in $info.data.features) {
        if ($f.typeName -eq 'Revolution') { $hasRevolve = $true; break }
    }
    if (-not $hasRevolve) { throw "Revolution feature not found in: $($info.data.features.typeName -join ', ')" }
    Write-Host ("[ok] geometry verified: bbox {0}x{1}x{2} mm (Y=D=40 confirms sphere not hemisphere) + Revolution feature" -f $size.x, $size.y, $size.z)

    # ── happy: tiny D5 sphere (sketch precision lower-bound area) ───────────
    $stdout = & $exe create-sphere --diameter 5 --out $smallSphere --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-sphere (D5) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    if (-not (Test-Path $smallSphere)) { throw "small sphere not created" }
    Write-Host ("[ok] D5 small sphere -> {0} ({1:N0} bytes)" -f $smallSphere, (Get-Item $smallSphere).Length)

    # ── validation: negative diameter (spec layer) ──────────────────────────
    & $exe create-sphere --diameter -10 --out $bigSphere 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for negative diameter" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'diameter')    { throw "error should reference diameter: $errMsg" }
    Write-Host "[ok] validation rejects negative diameter"

    # ── validation: missing parent directory (spec layer) ───────────────────
    $badPath = Join-Path $tmpDir ("no-such-{0}\sphere.sldprt" -f (Get-Random))
    & $exe create-sphere --diameter 30 --out $badPath 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for missing parent" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'parent directory') { throw "error should reference parent directory: $errMsg" }
    Write-Host "[ok] validation rejects missing parent directory"

    Write-Host '[ok] M27-sphere all checks passed'
} finally {
    foreach ($f in @($bigSphere, $smallSphere, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
