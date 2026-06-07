# L2 integration: M41 — read mates in inspect_assembly.
#
# inspect_assembly now returns a top-level mates[] list (decision a: inline, not a
# separate tool): each mate's name, type (coincident / distance / ...), the
# component instance names it connects, and value+unit for distance/angle mates.
# This is the read substrate for editing mate values (PR-4) and assembly resize
# (mate distances must scale with the parts).
#
#   Build: two cylinder instances + a distance mate (front, 25 mm) + a coincident
#          mate (top). inspect_assembly -> mateCount 2, the distance reads 25 mm
#          between the two instances, the coincident carries no value.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M41-read-mates.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cyl = Join-Path $tmpDir ("m41_cyl_{0}.sldprt" -f $rand)
$asm = Join-Path $tmpDir ("m41_asm_{0}.sldasm" -f $rand)

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
function Mate($d, [string]$type) { return (@($d.mates | Where-Object { $_.type -eq $type }))[0] }

try {
    Write-Host "== build two cylinder instances in an assembly =="
    Run @('create-cylinder','--diameter','30','--length','40','--out',$cyl) | Out-Null
    Run @('new-assembly','--out',$asm) | Out-Null
    Run @('add-component','--assembly',$asm,'--component',$cyl) | Out-Null
    Run @('add-component','--assembly',$asm,'--component',$cyl) | Out-Null

    # Use the real instance names (inspect first) so the mate selection is exact.
    $names = @((Run @('inspect-assembly','--input',$asm)).data.components | ForEach-Object { $_.name })
    Check "two component instances" ($names.Count -eq 2) "got $($names -join ',')"
    $c1 = $names[0]; $c2 = $names[1]

    Write-Host "== add a distance (front, 25 mm) + a coincident (top) mate =="
    $md = Run @('add-mate-distance','--assembly',$asm,'--component1',$c1,'--plane1','front','--component2',$c2,'--plane2','front','--distance','25')
    Check "distance mate added" ($md.status -eq 'ok') $md.message
    $mc = Run @('add-mate-coincident','--assembly',$asm,'--component1',$c1,'--plane1','top','--component2',$c2,'--plane2','top')
    Check "coincident mate added" ($mc.status -eq 'ok') $mc.message

    Write-Host "== inspect_assembly reads the mates back =="
    $d = (Run @('inspect-assembly','--input',$asm)).data
    Check "mateCount == 2" ($d.mateCount -eq 2) "got $($d.mateCount)"

    $dist = Mate $d 'distance'
    Check "distance mate present" ($null -ne $dist) "mates: $(($d.mates | ForEach-Object { $_.type }) -join ',')"
    Check "distance value == 25" ($dist.value -eq 25) "got $($dist.value)"
    Check "distance unit == mm" ($dist.unit -eq 'mm') "got $($dist.unit)"
    $dc = @($dist.components)
    Check "distance connects 2 components" ($dc.Count -eq 2) "got $($dc.Count)"
    Check "distance connects both instances" (($dc -contains $c1) -and ($dc -contains $c2)) "got $($dc -join ',')"

    $coin = Mate $d 'coincident'
    Check "coincident mate present" ($null -ne $coin) ""
    Check "coincident has no value (not distance/angle)" ($null -eq $coin.value) "got $($coin.value)"
    Check "coincident connects 2 components" (@($coin.components).Count -eq 2) "got $(@($coin.components).Count)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M41 read-mates -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($p in @($asm, $cyl)) { if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue } }
}
