# L2 integration: add-mate-distance — constrain two components' reference
# planes to be N mm apart. Same setup pattern as M18 (build asm via M16 +
# inspect to learn instance names), then mate.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M19-mate-distance.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cyl     = Join-Path $tmpDir ("dmate_cyl_{0}.sldprt"   -f (Get-Random))
$block   = Join-Path $tmpDir ("dmate_block_{0}.sldprt" -f (Get-Random))
$asm     = Join-Path $tmpDir ("dmate_asm_{0}.sldasm"   -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── setup: parts + 2-component assembly ─────────────────────────────────
    & $exe create-cylinder --diameter 20 --length 30 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cyl failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 60 --width 40 --height 10 --out $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }
    & $exe new-assembly --out $asm --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup asm failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asm --component $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-cyl failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asm --component $block --position-x 50 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-block failed: $(Get-Content $errFile -Raw)" }

    # Learn component instance names via inspect_assembly (LLM-realistic).
    $stdout = & $exe inspect-assembly --input $asm --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect failed: $(Get-Content $errFile -Raw)" }
    $insp = $stdout | ConvertFrom-Json
    $cylName = ($insp.data.components | Where-Object { $_.sourcePath -eq $cyl }).name
    $blockName = ($insp.data.components | Where-Object { $_.sourcePath -eq $block }).name
    if (-not $cylName -or -not $blockName) { throw "component names missing from inspect output" }
    Write-Host ("[ok] setup: components {0} + {1}" -f $cylName, $blockName)

    # ── happy: distance mate 25 mm (top@cyl ↔ top@block, closest) ──────────
    #   'closest' alignment lets SW pick the side that doesn't require flipping
    #   the existing component placement — robust default for L2 (LLMs can
    #   override with 'aligned' / 'anti-aligned' when geometry is known).
    $stdout = & $exe add-mate-distance `
        --assembly $asm `
        --component1 $cylName --plane1 top `
        --component2 $blockName --plane2 top `
        --distance 25 `
        --alignment closest `
        --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-mate-distance (25mm) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')              { throw "status: '$($r.status)'" }
    if ($r.path -ne $asm)                { throw "in-place path mismatch" }
    if ($r.message -notmatch '25')       { throw "message should mention 25: $($r.message)" }
    if ($r.message -notmatch '[Dd]istance') { throw "message should mention 'distance': $($r.message)" }
    Write-Host ("[ok] mate distance 25mm: top@{0} ↔ top@{1} (closest, in-place)" -f $cylName, $blockName)

    # ── validation: negative distance (spec layer) ──────────────────────────
    & $exe add-mate-distance `
        --assembly $asm `
        --component1 $cylName --plane1 front `
        --component2 $blockName --plane2 front `
        --distance -5 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for negative distance" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'distance')    { throw "error should reference 'distance': $errMsg" }
    Write-Host "[ok] validation rejects negative distance"

    # ── validation: same component on both sides ────────────────────────────
    & $exe add-mate-distance `
        --assembly $asm `
        --component1 $cylName --plane1 front `
        --component2 $cylName --plane2 top `
        --distance 10 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for self-mate" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'must differ') { throw "error should reference 'must differ': $errMsg" }
    Write-Host "[ok] validation rejects mating component to itself"

    # ── validation: unknown plane keyword (spec layer) ──────────────────────
    & $exe add-mate-distance `
        --assembly $asm `
        --component1 $cylName --plane1 bottom `
        --component2 $blockName --plane2 top `
        --distance 10 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for 'bottom' plane" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not recognized') { throw "error should reference 'not recognized': $errMsg" }
    Write-Host "[ok] validation rejects 'bottom' plane keyword"

    Write-Host '[ok] M19-mate-distance all checks passed'
} finally {
    foreach ($f in @($cyl, $block, $asm, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
