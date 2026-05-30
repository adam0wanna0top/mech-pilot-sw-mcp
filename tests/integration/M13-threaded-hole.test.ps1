# L2 integration: add-threaded-hole should drill a GB metric-coarse tap at
# the centroid of a part's ±Z end face. Uses HoleWizard5's GB-tap path with
# v1 PR #24's magic Value positions (Value7/8=1.0, Value11/12=-1.0).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M13-threaded-hole.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$srcA    = Join-Path $tmpDir ("thread_src_a_{0}.sldprt"  -f (Get-Random))
$srcB    = Join-Path $tmpDir ("thread_src_b_{0}.sldprt"  -f (Get-Random))
$copyA   = Join-Path $tmpDir ("thread_copy_a_{0}.sldprt" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

function New-SourceBlock([string]$path, [double]$l, [double]$w, [double]$h) {
    & $exe create-rectangular-block --length $l --width $w --height $h --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "setup create-rectangular-block failed: $(Get-Content $errFile -Raw)"
    }
    if (-not (Test-Path $path)) { throw "setup block not created: $path" }
}

try {
    # ── happy: M6 through-all tap on 40×40×10 block, copy ───────────────────
    New-SourceBlock $srcA 40 40 10
    $stdout = & $exe add-threaded-hole --input $srcA --thread M6 --out $copyA --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-threaded-hole (M6 through copy) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $copyA)             { throw "json path mismatch: '$($r.path)' vs '$copyA'" }
    if (-not (Test-Path $copyA))        { throw "tapped copy not created: $copyA" }
    if (-not (Test-Path $srcA))         { throw "source should be preserved: $srcA" }
    if ($r.message -notmatch 'M6')      { throw "message should mention M6: $($r.message)" }
    if ($r.message -notmatch 'through-all') { throw "message should mention through-all: $($r.message)" }
    Write-Host ("[ok] M6 through-all tap (copy) -> {0} ({1:N0} bytes)" -f $copyA, (Get-Item $copyA).Length)

    # ── happy: M4 blind 5 mm tap on 30×30×20 block, in-place ────────────────
    New-SourceBlock $srcB 30 30 20
    $stdout = & $exe add-threaded-hole --input $srcB --thread M4 --depth 5 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-threaded-hole (M4 blind in-place) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.path -ne $srcB)              { throw "in-place path should equal input: '$($r.path)' vs '$srcB'" }
    if ($r.message -notmatch 'M4')      { throw "message should mention M4: $($r.message)" }
    Write-Host ("[ok] M4 blind 5 mm tap (in-place) -> {0}" -f $srcB)

    # ── inspect verifies a hole feature was added ───────────────────────────
    $stdout = & $exe inspect-part --input $srcB --output json 2>$errFile
    $insp = $stdout | ConvertFrom-Json
    # Block alone = 2 features (sketch + extrude). After M4 tap should be >=3.
    if ($insp.data.featureCount -lt 3)  { throw "tap should add a feature; inspect featureCount=$($insp.data.featureCount)" }
    Write-Host ("[ok] inspect confirms tap feature added: {0} total features" -f $insp.data.featureCount)

    # ── validation: unsupported thread size (spec layer) ────────────────────
    & $exe add-threaded-hole --input $srcA --thread M7 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for M7 (skipped GB size)" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not in the GB') { throw "error message missing 'not in the GB': $errMsg" }
    Write-Host "[ok] validation rejects M7 (not on GB metric-coarse table)"

    # ── validation: nonexistent input (spec layer) ──────────────────────────
    $missing = Join-Path $tmpDir ("no_such_{0}.sldprt" -f ([Guid]::NewGuid()))
    & $exe add-threaded-hole --input $missing --thread M6 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')      { throw "error message missing 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    Write-Host '[ok] M13-threaded-hole all checks passed'
} finally {
    foreach ($f in @($srcA, $srcB, $copyA, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
