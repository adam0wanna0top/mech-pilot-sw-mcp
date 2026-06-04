# L2 integration: add-mate-angle should mate two components' reference planes
# at a given degree angle. Fourth mate type in the family — coincident /
# distance / concentric / **angle** — and the one that unlocks articulated
# assemblies (机械臂关节摆角 / 摇头风扇 / L 型支架夹角).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M25-mate-angle.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$link1 = Join-Path $tmpDir ("angmate_link1_{0}.sldprt" -f $rand)
$link2 = Join-Path $tmpDir ("angmate_link2_{0}.sldprt" -f $rand)
$asm = Join-Path $tmpDir ("angmate_asm_{0}.sldasm" -f $rand)
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── setup: 2 block "links" + assembly with both inserted ────────────────
    & $exe create-rectangular-block --length 50 --width 10 --height 10 --out $link1 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup link1 block failed: $(Get-Content $errFile -Raw)" }
    & $exe create-rectangular-block --length 30 --width 10 --height 10 --out $link2 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup link2 block failed: $(Get-Content $errFile -Raw)" }
    & $exe new-assembly --out $asm --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup assembly failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asm --component $link1 --position-x 0 --position-y 0 --position-z 0 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add link1 failed: $(Get-Content $errFile -Raw)" }
    & $exe add-component --assembly $asm --component $link2 --position-x 25 --position-y 0 --position-z 0 --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup add link2 failed: $(Get-Content $errFile -Raw)" }
    Write-Host "[setup] 2 blocks + assembly with both inserted -> $asm"

    # Get component instance names via inspect-assembly (the names follow the
    # base filename + "-1" SW convention but they vary by random suffix).
    $stdout = & $exe inspect-assembly --input $asm --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-assembly failed: $(Get-Content $errFile -Raw)" }
    $info = $stdout | ConvertFrom-Json
    if ($info.data.componentCount -ne 2) { throw "expected 2 components, got $($info.data.componentCount)" }
    # Find the link1 / link2 instance names (sorted asc by name doesn't help;
    # just match by sourcePath ends-with).
    $comp1Name = $null
    $comp2Name = $null
    foreach ($c in $info.data.components) {
        if ($c.sourcePath -like "*$([System.IO.Path]::GetFileName($link1))") { $comp1Name = $c.name }
        if ($c.sourcePath -like "*$([System.IO.Path]::GetFileName($link2))") { $comp2Name = $c.name }
    }
    if (-not $comp1Name) { throw "could not find link1 instance name" }
    if (-not $comp2Name) { throw "could not find link2 instance name" }
    Write-Host "[setup] link1='$comp1Name', link2='$comp2Name'"

    # ── happy: 90° right-angle mate between front planes (closest), in-place ──
    $stdout = & $exe add-mate-angle --assembly $asm --component1 $comp1Name --plane1 front --component2 $comp2Name --plane2 front --angle 90 --alignment closest --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-mate-angle 90° (closest) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $asm)               { throw "in-place path should equal input: '$($r.path)' vs '$asm'" }
    if ($r.message -notmatch '90')      { throw "message should mention angle 90: $($r.message)" }
    Write-Host "[ok] 90 deg right-angle mate front@link1 ↔ front@link2 (closest, in-place)"

    # ── validation: angle == 0 (spec layer) ─────────────────────────────────
    & $exe add-mate-angle --assembly $asm --component1 $comp1Name --plane1 front --component2 $comp2Name --plane2 front --angle 0 --alignment closest 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for angle=0" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'angle')      { throw "error should reference angle: $errMsg" }
    Write-Host "[ok] validation rejects angle = 0"

    # ── validation: angle == 180 (degenerate parallel) ──────────────────────
    & $exe add-mate-angle --assembly $asm --component1 $comp1Name --plane1 front --component2 $comp2Name --plane2 front --angle 180 --alignment closest 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for angle=180" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'angle')      { throw "error should reference angle: $errMsg" }
    Write-Host "[ok] validation rejects angle = 180 (degenerate)"

    # ── validation: self-mate (same component twice) ────────────────────────
    & $exe add-mate-angle --assembly $asm --component1 $comp1Name --plane1 front --component2 $comp1Name --plane2 top --angle 45 --alignment closest 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for self-mate" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'must differ') { throw "error should reference 'must differ': $errMsg" }
    Write-Host "[ok] validation rejects self-mate"

    # ── validation: invalid plane keyword ───────────────────────────────────
    & $exe add-mate-angle --assembly $asm --component1 $comp1Name --plane1 bottom --component2 $comp2Name --plane2 front --angle 45 --alignment closest 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for plane='bottom'" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not recognized') { throw "error should reference 'not recognized': $errMsg" }
    Write-Host "[ok] validation rejects plane='bottom'"

    Write-Host '[ok] M25-mate-angle all checks passed'
} finally {
    foreach ($f in @($link1, $link2, $asm, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
