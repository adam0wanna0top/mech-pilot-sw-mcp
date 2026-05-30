# L2 integration: add-axial-hole should drill a single ±Z cylindrical hole
# (through-all or blind) into an existing part. Each case creates a fresh
# source cylinder first. Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M8-axial-hole.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cylA    = Join-Path $tmpDir ("axhole_src_a_{0}.sldprt"  -f (Get-Random))
$cylB    = Join-Path $tmpDir ("axhole_src_b_{0}.sldprt"  -f (Get-Random))
$cylC    = Join-Path $tmpDir ("axhole_src_c_{0}.sldprt"  -f (Get-Random))
$copyA   = Join-Path $tmpDir ("axhole_copy_a_{0}.sldprt" -f (Get-Random))
$missing = Join-Path $tmpDir ("no_such_{0}.sldprt"       -f ([Guid]::NewGuid()))
$errFile = Join-Path $tmpDir 'stderr.txt'

function New-SourceCylinder([string]$path, [double]$d, [double]$len) {
    & $exe create-cylinder --diameter $d --length $len --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "setup create-cylinder failed (exit $LASTEXITCODE). stderr: $errTxt"
    }
    if (-not (Test-Path $path)) { throw "setup cylinder not created: $path" }
}

try {
    # ── happy: through-all hole, centered, save as copy ─────────────────────
    New-SourceCylinder $cylA 40 30
    $stdout = & $exe add-axial-hole --input $cylA --diameter 6.6 --out $copyA --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "add-axial-hole through-all (copy) exited $LASTEXITCODE. stderr: $errTxt; stdout: $stdout"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.status -ne 'ok')     { throw "json status: '$($result.status)'; stdout: $stdout" }
    if ($result.path -ne $copyA)     { throw "json path mismatch: '$($result.path)' vs '$copyA'" }
    if (-not (Test-Path $copyA))     { throw "drilled copy not created: $copyA" }
    if (-not (Test-Path $cylA))      { throw "source should be preserved: $cylA" }
    $size = (Get-Item $copyA).Length
    if ($size -lt 1024)              { throw ".sldprt suspiciously small: $size bytes" }
    Write-Host ("[ok] Φ6.6 through-all (copy) -> {0} ({1:N0} bytes)" -f $copyA, $size)

    # ── happy: blind hole, centered, in-place ───────────────────────────────
    New-SourceCylinder $cylB 40 30
    $stdout = & $exe add-axial-hole --input $cylB --diameter 5 --depth 10 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "add-axial-hole blind (in-place) exited $LASTEXITCODE. stderr: $errTxt"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.path -ne $cylB)      { throw "in-place path should equal input: '$($result.path)' vs '$cylB'" }
    if (-not (Test-Path $cylB))      { throw "in-place file missing after drill: $cylB" }
    if ($result.message -notmatch 'blind') { throw "message should mention blind: $($result.message)" }
    Write-Host ("[ok] Φ5 × 10 mm blind (in-place) -> {0} ({1:N0} bytes)" -f $cylB, (Get-Item $cylB).Length)

    # ── happy: through-all hole, offset (x,y), in-place ─────────────────────
    New-SourceCylinder $cylC 50 20
    $stdout = & $exe add-axial-hole --input $cylC --diameter 4 --position-x 10 --position-y -5 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "add-axial-hole offset exited $LASTEXITCODE. stderr: $errTxt"
    }
    Write-Host "[ok] Φ4 through-all at (10, -5) mm (in-place)"

    # ── validation: nonexistent input (spec layer) ──────────────────────────
    & $exe add-axial-hole --input $missing --diameter 5 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)         { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')   { throw "error message missing 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    # ── validation: negative diameter (spec layer) ──────────────────────────
    & $exe add-axial-hole --input $cylA --diameter -2 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)         { throw "expected non-zero exit for negative diameter" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'diameter'){ throw "error message missing 'diameter': $errMsg" }
    Write-Host "[ok] validation rejects negative diameter"

    Write-Host '[ok] M8-axial-hole all checks passed'
} finally {
    foreach ($f in @($cylA, $cylB, $cylC, $copyA, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
