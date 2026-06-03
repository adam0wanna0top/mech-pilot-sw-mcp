# L2 integration: add-mate-concentric — constrain two components' axial-Z
# cylindrical faces to share an axis. Both components must have a Z-axial
# cylindrical face: create_cylinder qualifies directly, create_flange's
# outer disk also has one. Block (no Z-axial cylinder) is used as a
# negative case.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M21-mate-concentric.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cylA    = Join-Path $tmpDir ("cmate_cylA_{0}.sldprt"   -f (Get-Random))
$cylB    = Join-Path $tmpDir ("cmate_cylB_{0}.sldprt"   -f (Get-Random))
$block   = Join-Path $tmpDir ("cmate_block_{0}.sldprt"  -f (Get-Random))
$asmOk   = Join-Path $tmpDir ("cmate_asmOk_{0}.sldasm"  -f (Get-Random))
$asmNeg  = Join-Path $tmpDir ("cmate_asmNeg_{0}.sldasm" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── setup: 2 cylinders + 1 block + 2 assemblies ─────────────────────────
    & $exe create-cylinder --diameter 20 --length 30 --out $cylA --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cylA failed: $(Get-Content $errFile -Raw)" }
    & $exe create-cylinder --diameter 25 --length 25 --out $cylB --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cylB failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }

    # Happy assembly: two cylinders
    & $exe new-assembly --out $asmOk --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "asmOk failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asmOk --component $cylA --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "asmOk add cylA failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asmOk --component $cylB --position-x 50 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "asmOk add cylB failed: $(Get-Content $errFile -Raw)" }

    # Negative assembly: cylinder + block (block has no Z-axial cylinder)
    & $exe new-assembly --out $asmNeg --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "asmNeg failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asmNeg --component $cylA --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "asmNeg add cylA failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asmNeg --component $block --position-x 50 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "asmNeg add block failed: $(Get-Content $errFile -Raw)" }

    # The component instance names for create_cylinder + add_component are
    # the .sldprt filename without extension followed by "-1" (SW convention),
    # but only with the leaf basename — derive once:
    $cylAName = ([IO.Path]::GetFileNameWithoutExtension($cylA)) + "-1"
    $cylBName = ([IO.Path]::GetFileNameWithoutExtension($cylB)) + "-1"
    $blockName = ([IO.Path]::GetFileNameWithoutExtension($block)) + "-1"
    Write-Host ("[ok] setup: {0} + {1} (happy asm), {0} + {2} (neg asm)" -f $cylAName, $cylBName, $blockName)

    # ── happy: concentric mate two cylinders ────────────────────────────────
    $stdout = & $exe add-mate-concentric `
        --assembly $asmOk `
        --component1 $cylAName --component2 $cylBName `
        --alignment closest `
        --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-mate-concentric (2 cyls) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')                  { throw "status: '$($r.status)'" }
    if ($r.path -ne $asmOk)                  { throw "in-place path mismatch" }
    if ($r.message -notmatch '[Cc]oncentric'){ throw "message should mention concentric: $($r.message)" }
    Write-Host ("[ok] concentric mate {0} ↔ {1} (closest, in-place)" -f $cylAName, $cylBName)

    # ── SW layer: block has no Z-axial cylindrical face ────────────────────
    & $exe add-mate-concentric `
        --assembly $asmNeg `
        --component1 $cylAName --component2 $blockName 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)                 { throw "expected non-zero exit for block (no cylinder face)" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'cylindrical')     { throw "error should reference 'cylindrical': $errMsg" }
    Write-Host "[ok] SW layer rejects block (no axial-Z cylindrical face)"

    # ── validation: same component on both sides ────────────────────────────
    & $exe add-mate-concentric `
        --assembly $asmOk `
        --component1 $cylAName --component2 $cylAName 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)                 { throw "expected non-zero exit for self-mate" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'must differ')     { throw "error should reference 'must differ': $errMsg" }
    Write-Host "[ok] validation rejects mating component to itself"

    # ── SW layer: unknown component name ────────────────────────────────────
    & $exe add-mate-concentric `
        --assembly $asmOk `
        --component1 "no_such_component-99" --component2 $cylBName 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)                 { throw "expected non-zero exit for unknown component" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not found')       { throw "error should reference 'not found': $errMsg" }
    Write-Host "[ok] SW layer rejects unknown component name"

    Write-Host '[ok] M21-mate-concentric all checks passed'
} finally {
    foreach ($f in @($cylA, $cylB, $block, $asmOk, $asmNeg, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
