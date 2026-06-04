# L2 integration: create-hemisphere should produce a solid hemisphere
# (1/4 sketch profile + centerline + 360° revolve around Y axis) and save
# as .sldprt. Validates the FeatureRevolve2 path — first non-prismatic
# geometry in mech-pilot-sw, paired with the 4 prismatic create_* tools.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M23-hemisphere.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$bigHemi = Join-Path $tmpDir ("hemi_big_{0}.sldprt"   -f (Get-Random))
$smallHemi = Join-Path $tmpDir ("hemi_small_{0}.sldprt" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy: D60 hemisphere → solid half-sphere, axis +Y ──────────────────
    #   Expected bbox X×Y×Z = 60 × 30 × 60 (Y ∈ [0, 30], X/Z ∈ [-30, 30]).
    $stdout = & $exe create-hemisphere --diameter 60 --out $bigHemi --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-hemisphere (D60) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')              { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $bigHemi)            { throw "json path mismatch: '$($r.path)' vs '$bigHemi'" }
    if (-not (Test-Path $bigHemi))       { throw ".sldprt not created: $bigHemi" }
    Write-Host ("[ok] D60 hemisphere -> {0} ({1:N0} bytes)" -f $bigHemi, (Get-Item $bigHemi).Length)

    # ── geometry verification via inspect-part: bbox + feature kind ─────────
    #   inspect-part returns featureCount + sizeMm + boundingBoxMm. For a
    #   D60 hemisphere created via Front-Plane sketch + revolve-around-Y we
    #   expect: 1 sketch + 1 Revolution feature = featureCount 2; sizeMm.x=60,
    #   sizeMm.y=30, sizeMm.z=60. This is the same geometry-not-just-API-ok
    #   verification pattern the M22 收尾 L3 established for pattern tools.
    $stdout = & $exe inspect-part --input $bigHemi --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-part failed: $(Get-Content $errFile -Raw)" }
    $info = $stdout | ConvertFrom-Json
    if ($info.data.featureCount -ne 2)   { throw "featureCount expected 2 (sketch + revolve), got $($info.data.featureCount)" }
    $size = $info.data.sizeMm
    if ([Math]::Abs($size.x - 60) -gt 0.01) { throw "size.x expected 60, got $($size.x)" }
    if ([Math]::Abs($size.y - 30) -gt 0.01) { throw "size.y expected 30 (=D/2), got $($size.y)" }
    if ([Math]::Abs($size.z - 60) -gt 0.01) { throw "size.z expected 60, got $($size.z)" }
    # Check the Revolution feature is actually there (not just any 2 features).
    $hasRevolve = $false
    foreach ($f in $info.data.features) {
        if ($f.typeName -eq 'Revolution') { $hasRevolve = $true; break }
    }
    if (-not $hasRevolve) { throw "Revolution feature not found in: $($info.data.features.typeName -join ', ')" }
    Write-Host ("[ok] geometry verified: bbox {0}x{1}x{2} mm (Y=D/2 confirms hemisphere) + Revolution feature" -f $size.x, $size.y, $size.z)

    # ── happy: tiny D5 hemisphere (sketch precision lower-bound area) ───────
    $stdout = & $exe create-hemisphere --diameter 5 --out $smallHemi --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "create-hemisphere (D5) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if (-not (Test-Path $smallHemi)) { throw "small hemisphere not created" }
    Write-Host ("[ok] D5 small hemisphere -> {0} ({1:N0} bytes)" -f $smallHemi, (Get-Item $smallHemi).Length)

    # ── validation: negative diameter (spec layer) ──────────────────────────
    & $exe create-hemisphere --diameter -10 --out $bigHemi 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for negative diameter" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'diameter')    { throw "error should reference diameter: $errMsg" }
    Write-Host "[ok] validation rejects negative diameter"

    # ── validation: missing parent directory (spec layer) ───────────────────
    $badPath = Join-Path $tmpDir ("no-such-{0}\hemi.sldprt" -f (Get-Random))
    & $exe create-hemisphere --diameter 30 --out $badPath 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for missing parent" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'parent directory') { throw "error should reference parent directory: $errMsg" }
    Write-Host "[ok] validation rejects missing parent directory"

    Write-Host '[ok] M23-hemisphere all checks passed'
} finally {
    foreach ($f in @($bigHemi, $smallHemi, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
