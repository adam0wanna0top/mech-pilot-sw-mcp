# L2 integration: inspect-assembly — read structured metadata from a .sldasm
# (component list with instance names + world positions). Builds assembly via
# M16 tools (new-assembly + add-component) first.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M17-inspect-assembly.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cyl     = Join-Path $tmpDir ("ins_cyl_{0}.sldprt"   -f (Get-Random))
$block   = Join-Path $tmpDir ("ins_block_{0}.sldprt" -f (Get-Random))
$emptyAsm  = Join-Path $tmpDir ("ins_empty_{0}.sldasm"  -f (Get-Random))
$twoCompAsm = Join-Path $tmpDir ("ins_two_{0}.sldasm"   -f (Get-Random))
$wrongExt   = Join-Path $tmpDir ("ins_wrong_{0}.sldprt" -f (Get-Random))
$missing    = Join-Path $tmpDir ("no_such_{0}.sldasm"   -f ([Guid]::NewGuid()))
$errFile    = Join-Path $tmpDir 'stderr.txt'

# Tolerance for SW position round-trip in mm.
$posTol = 0.01

try {
    # ── setup: build an empty assembly + 2-component assembly ───────────────
    & $exe create-cylinder --diameter 20 --length 30 --out $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cylinder failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 40 --width 30 --height 10 --out $block --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }

    & $exe new-assembly --out $emptyAsm --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup empty asm failed: $(Get-Content $errFile -Raw)" }
    & $exe new-assembly --out $twoCompAsm --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup two-comp asm failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $twoCompAsm --component $cyl --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-cyl failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $twoCompAsm --component $block --position-x 50 --position-y 10 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add-block failed: $(Get-Content $errFile -Raw)" }

    # ── happy: inspect empty assembly ───────────────────────────────────────
    $stdout = & $exe inspect-assembly --input $emptyAsm --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "inspect-assembly (empty) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')           { throw "status: '$($r.status)'" }
    if ($null -eq $r.data)            { throw "data block missing" }
    if ($r.data.componentCount -ne 0) { throw "empty asm should have 0 components, got $($r.data.componentCount)" }
    if ($r.message -notmatch 'empty') { throw "message should mention 'empty': $($r.message)" }
    Write-Host "[ok] inspect empty assembly: 0 components"

    # ── happy: inspect 2-component assembly + verify positions ──────────────
    $stdout = & $exe inspect-assembly --input $twoCompAsm --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "inspect-assembly (2-comp) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.data.componentCount -ne 2) { throw "expected 2 components, got $($r.data.componentCount)" }

    # Identify cylinder vs block by source path (sourcePath is absolute).
    $cylComp = $r.data.components | Where-Object { $_.sourcePath -eq $cyl }
    $blockComp = $r.data.components | Where-Object { $_.sourcePath -eq $block }
    if ($null -eq $cylComp)   { throw "cylinder component missing from list: $($r.data.components | ConvertTo-Json)" }
    if ($null -eq $blockComp) { throw "block component missing from list" }

    # Note: positionMm reports the component **frame origin** in world coords,
    # not the centroid. add_component(0,0,0) for a +Z-extruded part anchors
    # the centroid at (0,0,0) → frame origin sits at z = -height/2 (cyl L30
    # → z=-15; block H10 → z=-5). We assert X/Y match the add_component
    # input (SW placement is direct on those axes) and just sanity-check
    # that Z is finite + within reasonable bounds.

    # Cylinder add_component(0,0,0) — X/Y must be 0; Z is SW's component-frame
    # offset (= -L/2 for +Z extruded cyl).
    if ([Math]::Abs($cylComp.positionMm.x) -gt $posTol -or
        [Math]::Abs($cylComp.positionMm.y) -gt $posTol) {
        throw "cylinder positionMm X/Y should be (0,0), got $($cylComp.positionMm | ConvertTo-Json)"
    }
    if ([Math]::Abs($cylComp.positionMm.z) -gt 100) {
        throw "cylinder positionMm.z out of sane range: $($cylComp.positionMm.z)"
    }
    # Block add_component(50, 10, 0) — X/Y must match input.
    if ([Math]::Abs($blockComp.positionMm.x - 50) -gt $posTol -or
        [Math]::Abs($blockComp.positionMm.y - 10) -gt $posTol) {
        throw "block positionMm X/Y should be (50,10), got $($blockComp.positionMm | ConvertTo-Json)"
    }
    if ([Math]::Abs($blockComp.positionMm.z) -gt 100) {
        throw "block positionMm.z out of sane range: $($blockComp.positionMm.z)"
    }

    # Instance names should reference the source filenames
    if ($cylComp.name -notlike '*ins_cyl*')   { throw "cylinder instance name unexpected: $($cylComp.name)" }
    if ($blockComp.name -notlike '*ins_block*') { throw "block instance name unexpected: $($blockComp.name)" }
    Write-Host ("[ok] inspect 2-comp assembly: cyl@(0,0,0) + block@(50,10,0); instances {0} / {1}" `
        -f $cylComp.name, $blockComp.name)

    # ── validation: nonexistent input ───────────────────────────────────────
    & $exe inspect-assembly --input $missing 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')    { throw "error should reference 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    # ── validation: wrong extension (.sldprt) hints inspect_part ────────────
    & $exe inspect-assembly --input $wrongExt 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)          { throw "expected non-zero exit for .sldprt input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'inspect_part') { throw "error should hint inspect_part: $errMsg" }
    Write-Host "[ok] validation hints LLM to use inspect_part for .sldprt"

    Write-Host '[ok] M17-inspect-assembly all checks passed'
} finally {
    foreach ($f in @($cyl, $block, $emptyAsm, $twoCompAsm, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
