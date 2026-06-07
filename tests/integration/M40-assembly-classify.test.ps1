# L2 integration: M40 — component classification in inspect_assembly.
#
# inspect_assembly now tags each component with:
#   kind               ourPart | imported | subassembly | unknown
#   fileName           basename of the source path
#   standardCandidate  name looks like a standard fastener/bearing (hint)
#   editableDimensions modify_feature handles (ourPart only)
# so the resize orchestrator can see which components it may edit vs leave fixed.
#
# This covers the SW wiring (GetModelDoc2 -> feature walk -> classify; sub-assembly
# detection; name heuristic). The "imported" kind is unit-tested in L1
# (PartKind.ClassifyPart on the MBimport node) and was confirmed by the M40 probe;
# fabricating a live imported component needs a STEP import tool (not yet built).
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M40-assembly-classify.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cyl = Join-Path $tmpDir ("m40_cyl_{0}.sldprt" -f $rand)
$iso = Join-Path $tmpDir ("ISO_4762_M6x20_{0}.sldprt" -f $rand)   # standard-looking name
$sub = Join-Path $tmpDir ("m40_sub_{0}.sldasm" -f $rand)
$top = Join-Path $tmpDir ("m40_top_{0}.sldasm" -f $rand)

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
function Comp($d, [string]$pattern) { return (@($d.components | Where-Object { $_.fileName -like $pattern }))[0] }

try {
    Write-Host "== build parts + a sub-assembly + a top assembly =="
    Run @('create-cylinder','--diameter','30','--length','40','--out',$cyl) | Out-Null
    Run @('create-cylinder','--diameter','8','--length','20','--out',$iso) | Out-Null
    Run @('new-assembly','--out',$sub) | Out-Null
    Run @('add-component','--assembly',$sub,'--component',$cyl) | Out-Null
    Run @('new-assembly','--out',$top) | Out-Null
    Run @('add-component','--assembly',$top,'--component',$cyl) | Out-Null
    Run @('add-component','--assembly',$top,'--component',$iso) | Out-Null
    Run @('add-component','--assembly',$top,'--component',$sub) | Out-Null

    $d = (Run @('inspect-assembly','--input',$top)).data
    Check "top has 3 components" ($d.componentCount -eq 3) "got $($d.componentCount)"

    Write-Host "== our parametric cylinder: kind=ourPart, not standard, has editable dims =="
    $c = Comp $d "*m40_cyl_*"
    Check "cyl found" ($null -ne $c) "components: $(($d.components | ForEach-Object { $_.fileName }) -join ',')"
    Check "cyl kind == ourPart" ($c.kind -eq 'ourPart') "got $($c.kind)"
    Check "cyl standardCandidate == false" ($c.standardCandidate -eq $false) "got $($c.standardCandidate)"
    $cd = @($c.editableDimensions)
    Check "cyl has >= 1 editable dim" ($cd.Count -ge 1) "got $($cd.Count)"
    Check "cyl dim name is a D1@ handle" ($cd[0].name -like 'D1@*') "got $($cd[0].name)"
    Check "cyl depth dim == 40 mm" (@($cd | Where-Object { $_.value -eq 40 -and $_.unit -eq 'mm' }).Count -ge 1) "dims: $(($cd | ForEach-Object { "$($_.name)=$($_.value)$($_.unit)" }) -join ',')"

    Write-Host "== standard-named part: same kind=ourPart but standardCandidate=true (name hint) =="
    $i = Comp $d "ISO_4762*"
    Check "iso-named found" ($null -ne $i) ""
    Check "iso-named kind == ourPart" ($i.kind -eq 'ourPart') "got $($i.kind)"
    Check "iso-named standardCandidate == true" ($i.standardCandidate -eq $true) "got $($i.standardCandidate)"

    Write-Host "== sub-assembly component: kind=subassembly, no editable dims =="
    $s = Comp $d "*m40_sub_*"
    Check "sub found" ($null -ne $s) ""
    Check "sub kind == subassembly" ($s.kind -eq 'subassembly') "got $($s.kind)"
    Check "sub has 0 editable dims" (@($s.editableDimensions).Count -eq 0) "got $(@($s.editableDimensions).Count)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M40 assembly-classify -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($p in @($top, $sub, $cyl, $iso)) { if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue } }
}
