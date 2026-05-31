# L2 integration: add-counterbore + add-countersink — GB/T 152.3 / 152.2
# sinks via HoleWizard5. Tests cover CB M6/M4 + CSK M8 + M3 CSK rejection
# (SW GB DB missing) + general validation.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M14-sinks.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$srcA    = Join-Path $tmpDir ("sink_src_a_{0}.sldprt"  -f (Get-Random))
$srcB    = Join-Path $tmpDir ("sink_src_b_{0}.sldprt"  -f (Get-Random))
$srcC    = Join-Path $tmpDir ("sink_src_c_{0}.sldprt"  -f (Get-Random))
$copyA   = Join-Path $tmpDir ("sink_copy_a_{0}.sldprt" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

function New-SourceBlock([string]$path, [double]$l, [double]$w, [double]$h) {
    & $exe create-rectangular-block --length $l --width $w --height $h --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }
    if (-not (Test-Path $path)) { throw "block not created: $path" }
}

try {
    # ── happy: M6 counterbore through-all on 40×40×20 block, save as copy ───
    New-SourceBlock $srcA 40 40 20
    $stdout = & $exe add-counterbore --input $srcA --thread M6 --out $copyA --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-counterbore (M6 through copy) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $copyA)             { throw "json path mismatch: '$($r.path)' vs '$copyA'" }
    if (-not (Test-Path $copyA))        { throw "counterbored copy not created: $copyA" }
    if ($r.message -notmatch 'M6')      { throw "message should mention M6: $($r.message)" }
    if ($r.message -notmatch 'counterbore') { throw "message should mention counterbore: $($r.message)" }
    Write-Host ("[ok] M6 counterbore through-all (copy) -> {0} ({1:N0} bytes)" -f $copyA, (Get-Item $copyA).Length)

    # ── happy: M4 counterbore blind 8mm, in-place ───────────────────────────
    New-SourceBlock $srcB 40 40 15
    $stdout = & $exe add-counterbore --input $srcB --thread M4 --depth 8 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-counterbore (M4 blind in-place) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.path -ne $srcB)              { throw "in-place path should equal input: '$($r.path)' vs '$srcB'" }
    Write-Host ("[ok] M4 counterbore blind 8mm (in-place) -> {0}" -f $srcB)

    # ── happy: M8 countersink through-all, in-place ─────────────────────────
    New-SourceBlock $srcC 50 50 15
    $stdout = & $exe add-countersink --input $srcC --thread M8 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-countersink (M8 through in-place) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.path -ne $srcC)              { throw "in-place path should equal input: '$($r.path)' vs '$srcC'" }
    if ($r.message -notmatch 'M8')      { throw "message should mention M8: $($r.message)" }
    if ($r.message -notmatch 'countersink') { throw "message should mention countersink: $($r.message)" }
    Write-Host ("[ok] M8 countersink through-all (in-place) -> {0}" -f $srcC)

    # ── validation: CSK rejects M3 (SW GB DB missing M3-M5) ─────────────────
    & $exe add-countersink --input $srcA --thread M3 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for M3 countersink" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not supported') { throw "error should reference 'not supported': $errMsg" }
    Write-Host "[ok] add-countersink rejects M3 (SW GB DB missing small sizes)"

    # ── validation: CB rejects unsupported thread (M7) ──────────────────────
    & $exe add-counterbore --input $srcA --thread M7 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for M7 counterbore" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not in the GB') { throw "error should reference 'not in the GB': $errMsg" }
    Write-Host "[ok] add-counterbore rejects M7 (not in GB/T 152.3 table)"

    Write-Host '[ok] M14-sinks all checks passed'
} finally {
    foreach ($f in @($srcA, $srcB, $srcC, $copyA, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
