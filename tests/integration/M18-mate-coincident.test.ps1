# L2 integration: add-mate-coincident — constrain two components' reference
# planes to be coincident in an assembly. Builds the assembly via M16
# tools + M17 inspect to learn instance names, then mate.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M18-mate-coincident.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cyl     = Join-Path $tmpDir ("mate_cyl_{0}.sldprt"   -f (Get-Random))
$block   = Join-Path $tmpDir ("mate_block_{0}.sldprt" -f (Get-Random))
$asm     = Join-Path $tmpDir ("mate_asm_{0}.sldasm"   -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── setup: parts + 2-component assembly ─────────────────────────────────
    & $exe create-cylinder --diameter 20 --length 30 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cyl failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }
    & $exe new-assembly --out $asm --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup asm failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asm --component $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-cyl failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asm --component $block --position-x 50 --position-y 20 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-block failed: $(Get-Content $errFile -Raw)" }

    # Learn component instance names via inspect_assembly (LLM-realistic path)
    $stdout = & $exe inspect-assembly --input $asm --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect failed: $(Get-Content $errFile -Raw)" }
    $insp = $stdout | ConvertFrom-Json
    $cylName = ($insp.data.components | Where-Object { $_.sourcePath -eq $cyl }).name
    $blockName = ($insp.data.components | Where-Object { $_.sourcePath -eq $block }).name
    if (-not $cylName)   { throw "could not find cyl component name in inspect output" }
    if (-not $blockName) { throw "could not find block component name in inspect output" }
    Write-Host ("[ok] setup: components {0} + {1}" -f $cylName, $blockName)

    # ── happy: mate cyl.Front to block.Top (aligned), in-place ──────────────
    $stdout = & $exe add-mate-coincident `
        --assembly $asm `
        --component1 $cylName --plane1 front `
        --component2 $blockName --plane2 top `
        --alignment aligned `
        --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-mate-coincident (front-top aligned) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')                     { throw "status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $asm)                       { throw "in-place path mismatch: '$($r.path)' vs '$asm'" }
    if ($r.message -notmatch 'coincident' -and $r.message -notmatch 'Coincident') { throw "message missing 'coincident': $($r.message)" }
    Write-Host ("[ok] mate front@{0} ↔ top@{1} (aligned, in-place)" -f $cylName, $blockName)

    # ── validation: same component on both sides ────────────────────────────
    & $exe add-mate-coincident `
        --assembly $asm `
        --component1 $cylName --plane1 front `
        --component2 $cylName --plane2 top `
        --alignment aligned 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for self-mate" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'must differ') { throw "error should reference 'must differ': $errMsg" }
    Write-Host "[ok] validation rejects mating component to itself"

    # ── validation: unknown component name (SW layer) ───────────────────────
    & $exe add-mate-coincident `
        --assembly $asm `
        --component1 "no_such_component-99" --plane1 front `
        --component2 $blockName --plane2 top 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for unknown component" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'Could not select') { throw "error should reference selection failure: $errMsg" }
    Write-Host "[ok] SW layer rejects unknown component name (no plane found)"

    # ── validation: unknown plane keyword (spec layer) ──────────────────────
    & $exe add-mate-coincident `
        --assembly $asm `
        --component1 $cylName --plane1 bottom `
        --component2 $blockName --plane2 top 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)            { throw "expected non-zero exit for 'bottom' plane" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not recognized') { throw "error should reference 'not recognized': $errMsg" }
    Write-Host "[ok] validation rejects 'bottom' plane keyword"

    Write-Host '[ok] M18-mate-coincident all checks passed'
} finally {
    foreach ($f in @($cyl, $block, $asm, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
