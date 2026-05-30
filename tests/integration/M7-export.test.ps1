# L2 integration: export-part should hand a .sldprt off to STEP / STL / IGES /
# Parasolid via SW's Extension.SaveAs dispatch (extension picks the format).
# Requires SolidWorks to be installed.
# Run: pwsh ./tests/integration/M7-export.test.ps1

# 'Continue' (not 'Stop') because we drive a native binary that legitimately
# writes to stderr on validation failures (PowerShell 5.x treats native stderr
# as RemoteException under Stop, even with '2>' redirect).
$ErrorActionPreference = 'Continue'

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$src       = Join-Path $tmpDir ("export_src_{0}.sldprt"  -f (Get-Random))
$outStep   = Join-Path $tmpDir ("export_out_{0}.step"    -f (Get-Random))
$outStl    = Join-Path $tmpDir ("export_out_{0}.stl"     -f (Get-Random))
$outBad    = Join-Path $tmpDir ("export_out_{0}.obj"     -f (Get-Random))
$missing   = Join-Path $tmpDir ("no_such_{0}.sldprt"     -f ([Guid]::NewGuid()))
$errFile   = Join-Path $tmpDir 'stderr.txt'

function New-SourceCylinder([string]$path, [double]$d, [double]$len) {
    & $exe create-cylinder --diameter $d --length $len --out $path --output json 2>$errFile | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "setup create-cylinder failed (exit $LASTEXITCODE). stderr: $errTxt"
    }
    if (-not (Test-Path $path)) { throw "setup cylinder not created: $path" }
}

try {
    New-SourceCylinder $src 30 50

    # ── happy path: STEP ────────────────────────────────────────────────────
    $stdout = & $exe export-part --input $src --out $outStep --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "export-part STEP exited $LASTEXITCODE. stderr: $errTxt"
    }
    $result = $stdout | ConvertFrom-Json
    if ($result.status -ne 'ok')     { throw "json status: '$($result.status)'" }
    if ($result.path -ne $outStep)   { throw "json path mismatch: '$($result.path)' vs '$outStep'" }
    if (-not (Test-Path $outStep))   { throw "STEP file not created: $outStep" }
    if (-not (Test-Path $src))       { throw "source .sldprt should be preserved: $src" }
    $size = (Get-Item $outStep).Length
    if ($size -lt 512)               { throw "STEP file suspiciously small: $size bytes" }
    $head = Get-Content $outStep -TotalCount 1
    if ($head -notmatch '^ISO-10303') { throw "STEP file header not ISO-10303: '$head'" }
    Write-Host ("[ok] export STEP -> {0} ({1:N0} bytes, ISO-10303 header)" -f $outStep, $size)

    # ── happy path: STL (proves multi-format dispatch — same SaveAs call) ───
    $stdout = & $exe export-part --input $src --out $outStl --output json 2>$errFile
    if ($LASTEXITCODE -ne 0) {
        $errTxt = Get-Content $errFile -Raw -ErrorAction SilentlyContinue
        throw "export-part STL exited $LASTEXITCODE. stderr: $errTxt"
    }
    if (-not (Test-Path $outStl))    { throw "STL file not created: $outStl" }
    $stlSize = (Get-Item $outStl).Length
    if ($stlSize -lt 512)            { throw "STL file suspiciously small: $stlSize bytes" }
    Write-Host ("[ok] export STL  -> {0} ({1:N0} bytes)" -f $outStl, $stlSize)

    # ── validation: unsupported output extension (spec layer) ───────────────
    & $exe export-part --input $src --out $outBad 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)         { throw "expected non-zero exit for .obj output" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'supported neutral format') {
        throw "error message missing 'supported neutral format': $errMsg"
    }
    Write-Host "[ok] validation rejects unsupported extension (.obj)"

    # ── validation: nonexistent input (spec layer) ──────────────────────────
    & $exe export-part --input $missing --out $outStep 2>$errFile | Out-Null
    if ($LASTEXITCODE -eq 0)         { throw "expected non-zero exit for nonexistent input" }
    $errMsg = Get-Content $errFile -Raw
    if ($errMsg -notmatch 'exist')   { throw "error message missing 'exist': $errMsg" }
    Write-Host "[ok] validation rejects nonexistent input"

    Write-Host '[ok] M7-export all checks passed'
} finally {
    foreach ($f in @($src, $outStep, $outStl, $outBad, $errFile)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
