# L2 integration: create-flange should produce a real .sldprt with the
# canonical PR #35 reference geometry (D80×t10 flange, ø30 center hole,
# 4 × M6 holes on PCD55), and reject invalid specs.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M3-flange.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native
# stderr as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$canonical = Join-Path $tmpDir ("flange_canonical_{0}.sldprt" -f (Get-Random))
$solid     = Join-Path $tmpDir ("flange_solid_{0}.sldprt"     -f (Get-Random))
$boltsOnly = Join-Path $tmpDir ("flange_boltsonly_{0}.sldprt" -f (Get-Random))
$errFile   = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy path: canonical PR #35 flange ─────────────────────────────────
    $stdout = & $exe create-flange `
        --outer 80 --thickness 10 `
        --center-hole 30 `
        --bolt-count 4 --bolt-d 6 --pcd 55 `
        --out $canonical --output json 2>$errFile
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "create-flange (canonical) exited $exit. stderr: $errTxt; stdout: $stdout"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.status -ne 'ok')        { throw "json status: '$($result.status)'; stdout was: $stdout" }
    if ($result.path -ne $canonical)    { throw "json path mismatch: '$($result.path)' vs '$canonical'" }
    if (-not (Test-Path $canonical))    { throw "file not created: $canonical" }
    $size = (Get-Item $canonical).Length
    if ($size -lt 1024)                 { throw "canonical .sldprt suspiciously small: $size bytes" }
    Write-Host ("[ok] canonical flange D80 t10 ø30 + 4xM6 PCD55 -> {0} ({1:N0} bytes)" -f $canonical, $size)

    # ── happy path: solid disk (no holes) ───────────────────────────────────
    $stdout = & $exe create-flange `
        --outer 60 --thickness 8 `
        --out $solid --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "create-flange (solid) exited $LASTEXITCODE. stderr: $errTxt"
    }
    if (-not (Test-Path $solid))        { throw "solid file not created: $solid" }
    Write-Host ("[ok] solid disk D60 t8 (no holes) -> {0} ({1:N0} bytes)" -f $solid, (Get-Item $solid).Length)

    # ── happy path: bolts only (no center hole) ─────────────────────────────
    $stdout = & $exe create-flange `
        --outer 100 --thickness 12 `
        --bolt-count 6 --bolt-d 8 --pcd 70 `
        --out $boltsOnly --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "create-flange (bolts only) exited $LASTEXITCODE. stderr: $errTxt"
    }
    if (-not (Test-Path $boltsOnly))    { throw "bolts-only file not created: $boltsOnly" }
    Write-Host ("[ok] bolts-only D100 t12 6xM8 PCD70 -> {0} ({1:N0} bytes)" -f $boltsOnly, (Get-Item $boltsOnly).Length)

    # ── validation: PCD too large (extends past outer) ──────────────────────
    $bad1 = Join-Path $tmpDir 'flange_bad_pcd.sldprt'
    & $exe create-flange --outer 50 --thickness 10 `
        --bolt-count 4 --bolt-d 6 --pcd 60 `
        --out $bad1 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit when PCD > outer" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'PCD')        { throw "error message missing 'PCD': $errMsg" }
    if (Test-Path $bad1)                { throw "bad-spec file should not have been created" }
    Write-Host "[ok] validation rejects PCD > outer (exit=$LASTEXITCODE)"

    # ── validation: PCD too small (overlaps center hole) ────────────────────
    $bad2 = Join-Path $tmpDir 'flange_bad_pcd2.sldprt'
    & $exe create-flange --outer 80 --thickness 10 `
        --center-hole 40 --bolt-count 4 --bolt-d 6 --pcd 38 `
        --out $bad2 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit when PCD overlaps center hole" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'PCD')        { throw "error message missing 'PCD': $errMsg" }
    Write-Host "[ok] validation rejects PCD overlapping center hole"

    # ── validation: bolt count = 0 but bolt geometry set (footgun) ──────────
    $bad3 = Join-Path $tmpDir 'flange_bad_bc.sldprt'
    & $exe create-flange --outer 80 --thickness 10 `
        --bolt-count 0 --bolt-d 6 --pcd 55 `
        --out $bad3 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit when boltCount=0 but bolt geometry set" }
    Write-Host "[ok] validation catches boltCount=0 footgun"

    Write-Host '[ok] M3-flange all checks passed'
} finally {
    foreach ($f in @($canonical, $solid, $boltsOnly, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
