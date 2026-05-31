# L2 integration: new-assembly + add-component — create an empty .sldasm
# then drop in two parts at different positions. Verifies AddComponent5
# works once the parts are pre-opened via OpenDoc6 (v1 PR #9 lesson).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M16-assembly.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cyl     = Join-Path $tmpDir ("asm_cyl_{0}.sldprt"   -f (Get-Random))
$block   = Join-Path $tmpDir ("asm_block_{0}.sldprt" -f (Get-Random))
$asm     = Join-Path $tmpDir ("asm_{0}.sldasm"       -f (Get-Random))
$missing = Join-Path $tmpDir ("no_such_{0}.sldasm"   -f ([Guid]::NewGuid()))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── setup: 2 parts to insert ────────────────────────────────────────────
    & $exe create-cylinder --diameter 20 --length 30 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cylinder failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }

    # ── happy: new_assembly creates empty .sldasm ───────────────────────────
    $stdout = & $exe new-assembly --out $asm --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "new-assembly exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')        { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $asm)          { throw "json path mismatch: '$($r.path)' vs '$asm'" }
    if (-not (Test-Path $asm))     { throw "assembly not created: $asm" }
    $emptyAsmSize = (Get-Item $asm).Length
    if ($emptyAsmSize -lt 1024)    { throw "empty .sldasm suspiciously small: $emptyAsmSize bytes" }
    Write-Host ("[ok] new_assembly empty -> {0} ({1:N0} bytes)" -f $asm, $emptyAsmSize)

    # ── happy: add_component cylinder at (0, 0, 0) ──────────────────────────
    $stdout = & $exe add-component --assembly $asm --component $cyl --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-component (cyl) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')        { throw "add-component cyl status: '$($r.status)'" }
    if ($r.message -notmatch 'asm_cyl') { throw "message should mention component: $($r.message)" }
    $oneCompSize = (Get-Item $asm).Length
    if ($oneCompSize -le $emptyAsmSize) {
        throw "asm size should grow after adding component: $emptyAsmSize -> $oneCompSize"
    }
    Write-Host ("[ok] add_component cyl at (0,0,0) -> asm now {0:N0} bytes (+{1:N0})" -f $oneCompSize, ($oneCompSize - $emptyAsmSize))

    # ── happy: add_component block at (50, 0, 0) ────────────────────────────
    #   Note: .sldasm is SW's internal binary compressed format — file size
    #   doesn't grow monotonically with component count (SW may re-pack the
    #   container). We assert tool returns ok + message references position
    #   + size stays > empty-asm baseline, not strict growth.
    $stdout = & $exe add-component --assembly $asm --component $block --position-x 50 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-component (block) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')        { throw "add-component block status: '$($r.status)'" }
    if ($r.message -notmatch '50') { throw "message should mention position 50: $($r.message)" }
    $twoCompSize = (Get-Item $asm).Length
    if ($twoCompSize -le $emptyAsmSize) {
        throw "asm should stay populated after 2nd component (empty=$emptyAsmSize, now=$twoCompSize)"
    }
    Write-Host ("[ok] add_component block at (50,0,0) -> asm now {0:N0} bytes" -f $twoCompSize)

    # ── validation: add_component to nonexistent assembly ───────────────────
    & $exe add-component --assembly $missing --component $cyl 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)        { throw "expected non-zero exit for nonexistent assembly" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')  { throw "error should reference missing assembly: $errMsg" }
    Write-Host "[ok] validation rejects nonexistent assembly"

    # ── validation: new_assembly wrong extension ────────────────────────────
    $wrongExt = Join-Path $tmpDir "asm.sldprt"
    & $exe new-assembly --out $wrongExt 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)        { throw "expected non-zero exit for .sldprt save path" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch '\.sldasm') { throw "error should reference .sldasm: $errMsg" }
    Write-Host "[ok] validation rejects new-assembly --out .sldprt"

    Write-Host '[ok] M16-assembly all checks passed'
} finally {
    foreach ($f in @($cyl, $block, $asm, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
