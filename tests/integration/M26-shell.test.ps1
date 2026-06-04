# L2 integration: add-shell should hollow out an existing solid part with
# a uniform wall thickness, opening the +Z end face. M26 — first SW
# subtractive operation that produces LLM-irreplaceable geometry. Critical
# test point: IModelDoc2.InsertFeatureShell returns void with no
# success/failure signal — we verify via inspect-part that a Shell-type
# feature was actually added (M22 收尾 geometry-verification pattern).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M26-shell.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cylSrc = Join-Path $tmpDir ("shell_cyl_{0}.sldprt" -f $rand)
$cylCopy = Join-Path $tmpDir ("shell_cyl_copy_{0}.sldprt" -f $rand)
$blockSrc = Join-Path $tmpDir ("shell_block_{0}.sldprt" -f $rand)
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy: cylinder D40 L30 → shell 2mm inward, in-place ────────────────
    & $exe create-cylinder --diameter 40 --length 30 --out $cylSrc --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup cylinder failed: $(Get-Content $errFile -Raw)" }

    $stdout = & $exe add-shell --input $cylSrc --thickness 2 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-shell (D40 cyl, 2mm) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')              { throw "json status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $cylSrc)             { throw "in-place path should equal input: '$($r.path)' vs '$cylSrc'" }
    if ($r.message -notmatch 'inward')   { throw "message should mention 'inward' (default): $($r.message)" }
    Write-Host ("[ok] D40 cyl shelled 2mm inward (in-place) -> {0}" -f $cylSrc)

    # ── critical: geometry verification (M22 收尾 pattern for silent-fail risk) ──
    #   InsertFeatureShell returns void with no error signal; SW could no-op
    #   silently. Verify the shell really got applied:
    #     - featureCount = 3 (sketch + Extrusion + Shell)
    #     - feature list contains a typeName="Shell"
    $stdout = & $exe inspect-part --input $cylSrc --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-part after shell failed: $(Get-Content $errFile -Raw)" }
    $info = $stdout | ConvertFrom-Json
    if ($info.data.featureCount -ne 3) { throw "featureCount expected 3 (sketch + extrusion + shell), got $($info.data.featureCount)" }
    $hasShell = $false
    foreach ($f in $info.data.features) {
        if ($f.typeName -eq 'Shell') { $hasShell = $true; break }
    }
    if (-not $hasShell) { throw "Shell feature not found in: $($info.data.features.typeName -join ', ')" }
    Write-Host "[ok] geometry verified: featureCount=3 + Shell feature exists (defeats silent-fail risk)"

    # ── happy: block 50x30x20 → shell 1mm outward, save as copy ─────────────
    & $exe create-rectangular-block --length 50 --width 30 --height 20 --out $blockSrc --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup block failed: $(Get-Content $errFile -Raw)" }
    $stdout = & $exe add-shell --input $blockSrc --thickness 1 --outward --out $cylCopy --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-shell (block, 1mm outward, copy) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if (-not (Test-Path $cylCopy))       { throw "shelled copy not created: $cylCopy" }
    if (-not (Test-Path $blockSrc))      { throw "source should be preserved: $blockSrc" }
    if ($r.message -notmatch 'outward')  { throw "message should mention 'outward': $($r.message)" }
    Write-Host ("[ok] block shelled 1mm outward (copy) -> {0}" -f $cylCopy)

    # ── SW layer: very thin wall (0.5mm on D40 cylinder) ────────────────────
    & $exe create-cylinder --diameter 40 --length 30 --out $cylSrc --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup re-create cyl failed: $(Get-Content $errFile -Raw)" }
    $stdout = & $exe add-shell --input $cylSrc --thickness 0.5 --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "add-shell (D40 cyl, 0.5mm thin) exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    Write-Host "[ok] D40 cyl shelled 0.5mm (thin wall, in-place)"

    # ── validation: negative thickness (spec layer) ─────────────────────────
    & $exe add-shell --input $cylSrc --thickness -2 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for negative thickness" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'thickness')   { throw "error should reference thickness: $errMsg" }
    Write-Host "[ok] validation rejects negative thickness"

    # ── validation: > 100mm (unit-confusion guard) ──────────────────────────
    & $exe add-shell --input $cylSrc --thickness 1000 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for thickness > 100mm" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'range')       { throw "error should reference range: $errMsg" }
    Write-Host "[ok] validation rejects thickness 1000mm (unit-confusion guard)"

    Write-Host '[ok] M26-shell all checks passed'
} finally {
    foreach ($f in @($cylSrc, $cylCopy, $blockSrc, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
