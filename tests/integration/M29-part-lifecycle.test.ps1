# L2 integration: new_part + save_part bracket-pair should open a blank
# part, become SW's active doc, then save it to disk as an empty .sldprt
# and close it. M29 — entry of the generic primitives layer (vs. existing
# 7 parametric helpers that bundle new+sketch+feature+save into 1 call).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M29-part-lifecycle.test.ps1

$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$emptyPart = Join-Path $tmpDir ("empty_part_{0}.sldprt" -f (Get-Random))
$errFile = Join-Path $tmpDir 'stderr.txt'

try {
    # ── happy: new_part → save_part bracket-pair produces empty .sldprt ─────
    #   new_part has no parameters; save_part takes --out.
    #   Expected: a small .sldprt file with no body features, only the SW
    #   default RefPlanes / origin / etc.
    $stdout = & $exe new-part --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "new-part exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "new-part json status: '$($r.status)'" }
    if ($r.message -notmatch 'active')  { throw "new-part message should mention 'active doc': $($r.message)" }
    Write-Host "[ok] new-part opened a blank part (active doc)"

    $stdout = & $exe save-part --out $emptyPart --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        throw "save-part exited $LASTEXITCODE. stderr: $(Get-Content $errFile -Raw); stdout: $stdout"
    }
    $r = $stdout | ConvertFrom-Json
    if ($r.status -ne 'ok')             { throw "save-part json status: '$($r.status)'" }
    if ($r.path -ne $emptyPart)         { throw "save-part path mismatch: '$($r.path)' vs '$emptyPart'" }
    if (-not (Test-Path $emptyPart))    { throw "empty part not created: $emptyPart" }
    Write-Host ("[ok] save-part wrote .sldprt -> {0} ({1:N0} bytes)" -f $emptyPart, (Get-Item $emptyPart).Length)

    # ── geometry verification: inspect-part should report 0 user features ───
    #   An empty part has only the default RefPlanes / origin / Folders, all
    #   of which inspect_part's boot filter strips out. So featureCount == 0
    #   AND no solid bodies (bodyCount == 0).
    $stdout = & $exe inspect-part --input $emptyPart --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) { throw "inspect-part failed: $(Get-Content $errFile -Raw)" }
    $info = $stdout | ConvertFrom-Json
    if ($info.data.featureCount -ne 0) {
        throw "empty part featureCount expected 0, got $($info.data.featureCount): $($info.data.features.typeName -join ', ')"
    }
    if ($info.data.bodyCount -ne 0) {
        throw "empty part bodyCount expected 0, got $($info.data.bodyCount)"
    }
    Write-Host ("[ok] inspect-part on empty part: featureCount=0, bodyCount=0 (expected)")

    # ── save-part with no active doc should reject cleanly ──────────────────
    #   After the prior save-part, no doc is active. A fresh save-part call
    #   should fail with "no active document".
    & $exe save-part --out $emptyPart 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit: no active doc to save" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'active|No active') {
        throw "error should reference active doc: $errMsg"
    }
    Write-Host "[ok] save-part rejects when no active doc"

    # ── validation: negative spec layer (wrong extension) ───────────────────
    & $exe save-part --out "C:\tmp\wrong.step" 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)             { throw "expected non-zero exit for wrong extension" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch '\.sldprt')   { throw "error should reference .sldprt: $errMsg" }
    Write-Host "[ok] save-part rejects wrong extension at spec layer"

    Write-Host '[ok] M29-part-lifecycle all checks passed'
} finally {
    foreach ($f in @($emptyPart, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
