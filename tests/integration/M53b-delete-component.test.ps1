# L2 integration: M53-(2) delete_component — remove one component instance
# from an assembly by name, cascading its mates. Builds a 2-component + 1-mate
# assembly via M16/M18 tools, deletes one component, and verifies the instance
# AND its mate are gone and the change persisted.
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M53b-delete-component.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$cyl     = Join-Path $tmpDir ("del_cyl_{0}.sldprt"   -f (Get-Random))
$block   = Join-Path $tmpDir ("del_block_{0}.sldprt" -f (Get-Random))
$asm     = Join-Path $tmpDir ("del_asm_{0}.sldasm"   -f (Get-Random))
$missing = Join-Path $tmpDir ("no_such_{0}.sldasm"   -f ([Guid]::NewGuid()))
$errFile = Join-Path $tmpDir 'stderr_m53b.txt'

function Inspect($a) {
    $stdout = & $exe inspect-assembly --input $a --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    return ($stdout | ConvertFrom-Json).data
}

try {
    # ── setup: parts + 2-component assembly + 1 coincident mate ─────────────
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

    $d = Inspect $asm
    $cylName   = ($d.components | Where-Object { $_.sourcePath -eq $cyl }).name
    $blockName = ($d.components | Where-Object { $_.sourcePath -eq $block }).name
    if (-not $cylName -or -not $blockName) { throw "could not read instance names" }
    if ($d.componentCount -ne 2) { throw "expected 2 components, got $($d.componentCount)" }

    & $exe add-mate-coincident --assembly $asm `
        --component1 $cylName --plane1 front `
        --component2 $blockName --plane2 top `
        --alignment aligned --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "setup mate failed: $(Get-Content $errFile -Raw)" }

    $d = Inspect $asm
    if ($d.mateCount -lt 1) { throw "expected >=1 mate before delete, got $($d.mateCount)" }
    Write-Host ("[ok] setup: 2 components ({0} + {1}) + {2} mate(s)" -f $cylName, $blockName, $d.mateCount)

    # ── 1. delete the cylinder by name → count 2 -> 1, message reports drop ──
    $stdout = & $exe delete-component --assembly $asm --name $cylName --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "delete-component exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw)" }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')   { throw "status: '$($r.status)'; stdout: $stdout" }
    if ($r.path -ne $asm)     { throw "in-place path mismatch: '$($r.path)'" }
    if ($r.message -notmatch '2' -or $r.message -notmatch '1') { throw "message should report 2->1: $($r.message)" }
    Write-Host "[ok] delete-component removed '$cylName' (message reports 2 -> 1)"

    # ── 2. re-inspect: only the block remains, and the mate cascaded away ────
    $d = Inspect $asm
    if ($d.componentCount -ne 1) { throw "expected 1 component after delete, got $($d.componentCount)" }
    if (($d.components | Where-Object { $_.sourcePath -eq $cyl })) { throw "deleted cylinder still present" }
    if (-not ($d.components | Where-Object { $_.sourcePath -eq $block })) { throw "block should remain" }
    if ($d.mateCount -ne 0) { throw "mate referencing the deleted component should be gone, mateCount=$($d.mateCount)" }
    Write-Host "[ok] re-inspect: 1 component (block) remains; mate cascaded away (mateCount 0)"

    # ── 3. the component FILE on disk is untouched ──────────────────────────
    if (-not (Test-Path $cyl)) { throw "delete_component must NOT delete the source .sldprt on disk" }
    Write-Host "[ok] source cylinder .sldprt on disk is untouched"

    # ── 4. negative: unknown instance name lists available names ────────────
    & $exe delete-component --assembly $asm --name "no_such_instance-9" 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for unknown component name" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'not found')   { throw "error should say 'not found': $errMsg" }
    if ($errMsg -notmatch [regex]::Escape($blockName)) { throw "error should list available name '$blockName': $errMsg" }
    Write-Host "[ok] unknown name rejected; error lists available instance names"

    # ── 5. negative: nonexistent assembly ───────────────────────────────────
    & $exe delete-component --assembly $missing --name "x-1" 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "expected non-zero exit for nonexistent assembly" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist') { throw "error should reference 'exist': $errMsg" }
    Write-Host "[ok] nonexistent assembly rejected"

    Write-Host '[ok] M53b-delete-component all checks passed'
} finally {
    foreach ($f in @($cyl, $block, $asm, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
