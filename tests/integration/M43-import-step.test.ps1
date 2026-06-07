# L2 integration: M43 — import_step (neutral CAD -> .sldprt dumb body).
#
# Round-trips through SW's own exporter: create a cylinder, export it to STEP,
# import the STEP back as a part, and verify it is a DUMB body (MBimport node,
# no build features, 0 editable dims). Then insert it into an assembly and confirm
# inspect_assembly classifies it as 'imported' — the live imported-component
# coverage deferred from M40.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M43-import-step.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cyl = Join-Path $tmpDir ("m43_cyl_{0}.sldprt" -f $rand)
$step = Join-Path $tmpDir ("m43_cyl_{0}.step" -f $rand)
$imported = Join-Path $tmpDir ("m43_imported_{0}.sldprt" -f $rand)
$asm = Join-Path $tmpDir ("m43_asm_{0}.sldasm" -f $rand)

$script:fail = 0
function Check([string]$label, [bool]$cond, [string]$detail = '') {
    if ($cond) { Write-Host "[ok] $label" } else { Write-Host "[FAIL] $label $detail"; $script:fail++ }
}
function Run([string[]]$a) {
    $o = & $exe @a --output json 2>&1; $raw = ($o -join "`n")
    if ($LASTEXITCODE -ne 0) { throw "command failed: $($a -join ' ')`n$raw" }
    $obj = $null; try { $obj = $raw | ConvertFrom-Json } catch {}
    return $obj
}
function TryRun([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($o -join "`n") }
}
function HasFeat($d, [string]$type) { return (@($d.features | Where-Object { $_.typeName -eq $type })).Count -ge 1 }
function Comp($d, [string]$pattern) { return (@($d.components | Where-Object { $_.fileName -like $pattern }))[0] }

try {
    Write-Host "== import a STEP (round-trip via export_part) =="
    Run @('create-cylinder','--diameter','30','--length','40','--out',$cyl) | Out-Null
    Run @('export-part','--input',$cyl,'--out',$step) | Out-Null
    Check "step exported" (Test-Path $step)
    $r = Run @('import-step','--input',$step,'--out',$imported)
    Check "import reports dumb body" ($r.message -match 'dumb body') $r.message
    Check "imported .sldprt created" (Test-Path $imported)

    Write-Host "== inspect the imported part: dumb body, no editable dims =="
    $p = (Run @('inspect-part','--input',$imported)).data
    Check "imported: 1 solid body" ($p.bodyCount -eq 1) "got $($p.bodyCount)"
    Check "imported: has MBimport feature" (HasFeat $p 'MBimport') "features: $(($p.features | ForEach-Object { $_.typeName }) -join ',')"
    Check "imported: 0 editable dims (no build features)" ($p.editableDimensionCount -eq 0) "got $($p.editableDimensionCount)"

    Write-Host "== inspect_assembly classifies it as 'imported' (M40 live coverage) =="
    Run @('new-assembly','--out',$asm) | Out-Null
    Run @('add-component','--assembly',$asm,'--component',$imported) | Out-Null
    $d = (Run @('inspect-assembly','--input',$asm)).data
    $c = Comp $d "*m43_imported_*"
    Check "imported component found" ($null -ne $c) "components: $(($d.components | ForEach-Object { $_.fileName }) -join ',')"
    Check "component kind == imported" ($c.kind -eq 'imported') "got $($c.kind)"
    Check "component 0 editable dims" (@($c.editableDimensions).Count -eq 0) "got $(@($c.editableDimensions).Count)"
    Check "component standardCandidate == false" ($c.standardCandidate -eq $false) "got $($c.standardCandidate)"

    Write-Host "== negatives =="
    $bad1 = TryRun @('import-step','--input',(Join-Path $tmpDir ("nope_{0}.step" -f $rand)),'--out',$imported)
    Check "missing input rejected (non-zero)" ($bad1.Code -ne 0) "code=$($bad1.Code)"
    $bad2 = TryRun @('import-step','--input',$cyl,'--out',$imported)
    Check "wrong input ext (.sldprt) rejected" ($bad2.Code -ne 0) "code=$($bad2.Code)"
    Check "wrong-ext error mentions neutral" ($bad2.Out -match 'neutral') $bad2.Out

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M43 import-step -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($asm, $imported, $step, $cyl)) { if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }
}
