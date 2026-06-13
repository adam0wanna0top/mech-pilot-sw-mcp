# L2 integration: M53-(4) add_component idempotency guard (--skip-if-present).
# Proves a retried insert with --skip-if-present does NOT duplicate a component
# already in the assembly (ghost prevention), while a new part still inserts and
# multi-instance inserts still work without the flag. Also checks the dedup is
# path-normalized (a forward-slash path still matches SW's backslash store).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M53d-add-component-idempotent.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$block   = Join-Path $tmpDir ("idem_block_{0}.sldprt" -f (Get-Random))
$cyl     = Join-Path $tmpDir ("idem_cyl_{0}.sldprt"   -f (Get-Random))
$asm     = Join-Path $tmpDir ("idem_asm_{0}.sldasm"   -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr_m53d.txt'

function Count($a) {
    $stdout = & $exe inspect-assembly --input $a --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-assembly exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return [int]($stdout | ConvertFrom-Json).data.componentCount
}

try {
    # ── setup: 2 parts + empty assembly ─────────────────────────────────────
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }
    & $exe create-cylinder --diameter 20 --length 30 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cyl failed: $(Get-Content $errFile -Raw)" }
    & $exe new-assembly --out $asm --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup asm failed: $(Get-Content $errFile -Raw)" }

    # ── 1. first insert (no flag) → 1 component ─────────────────────────────
    & $exe add-component --assembly $asm --component $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "first add failed: $(Get-Content $errFile -Raw)" }
    if ((Count $asm) -ne 1) { throw "after first insert expected 1, got $(Count $asm)" }
    Write-Host "[ok] first insert: 1 component"

    # ── 2. retry SAME part with --skip-if-present → skipped, still 1 ─────────
    $stdout = & $exe add-component --assembly $asm --component $block --skip-if-present --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "skip-if-present retry exited non-zero: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.message -notmatch 'already present' -or $r.message -notmatch 'skipped') {
        throw "skip message should say 'already present ... skipped': $($r.message)"
    }
    if ((Count $asm) -ne 1) { throw "skip-if-present must NOT duplicate; expected 1, got $(Count $asm)" }
    Write-Host "[ok] retry same part with --skip-if-present: skipped, still 1 (no ghost)"

    # ── 3. --skip-if-present for a DIFFERENT part still inserts → 2 ──────────
    & $exe add-component --assembly $asm --component $cyl --skip-if-present --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "new-part skip add failed: $(Get-Content $errFile -Raw)" }
    if ((Count $asm) -ne 2) { throw "new part should insert under --skip-if-present; expected 2, got $(Count $asm)" }
    Write-Host "[ok] --skip-if-present for a new part still inserts: 2 components"

    # ── 4. WITHOUT the flag, a legitimate 2nd instance is allowed → 3 ───────
    & $exe add-component --assembly $asm --component $block --position-x 80 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "multi-instance add failed: $(Get-Content $errFile -Raw)" }
    if ((Count $asm) -ne 3) { throw "without flag a 2nd block instance is allowed; expected 3, got $(Count $asm)" }
    Write-Host "[ok] without flag: legitimate 2nd block instance allowed (3 components)"

    # ── 5. dedup is path-normalized: a forward-slash path still matches ──────
    $blockFwd = $block -replace '\\','/'
    $stdout = & $exe add-component --assembly $asm --component $blockFwd --skip-if-present --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "forward-slash skip exited non-zero: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.message -notmatch 'already present') { throw "forward-slash path should match existing instance: $($r.message)" }
    if ((Count $asm) -ne 3) { throw "forward-slash skip must not duplicate; expected 3, got $(Count $asm)" }
    Write-Host "[ok] forward-slash path still matches (path-normalized dedup): still 3"

    Write-Host '[ok] M53d-add-component-idempotent all checks passed'
} finally {
    foreach ($f in @($block, $cyl, $asm, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
