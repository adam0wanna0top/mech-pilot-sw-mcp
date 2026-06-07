# L2 integration: M42 — modify_mate (edit an existing mate's value + rebuild).
#
# The mate counterpart of modify_feature, for assembly resize (scale distance
# mates). modify_mate finds the mate, sets its display dimension's SystemValue,
# EditRebuild3, and saves.
#
#   A) distance mate 25 -> 40 (decisive: inspect reads 40 back); coincident mate
#      rejected (no editable value); non-existent mate rejected.
#   B) angle mate 30 -> 45 deg (covers the degree path).
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M42-modify-mate.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cyl = Join-Path $tmpDir ("m42_cyl_{0}.sldprt" -f $rand)
$asm = Join-Path $tmpDir ("m42_asm_{0}.sldasm" -f $rand)
$asmB = Join-Path $tmpDir ("m42_asmB_{0}.sldasm" -f $rand)

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
function Mate($d, [string]$type) { return (@($d.mates | Where-Object { $_.type -eq $type }))[0] }
function Names($asm) { return @((Run @('inspect-assembly','--input',$asm)).data.components | ForEach-Object { $_.name }) }

try {
    Write-Host "== A) distance mate 25 -> 40 + negatives =="
    Run @('create-cylinder','--diameter','30','--length','40','--out',$cyl) | Out-Null
    Run @('new-assembly','--out',$asm) | Out-Null
    Run @('add-component','--assembly',$asm,'--component',$cyl) | Out-Null
    Run @('add-component','--assembly',$asm,'--component',$cyl) | Out-Null
    $n = Names $asm; $c1 = $n[0]; $c2 = $n[1]
    Run @('add-mate-distance','--assembly',$asm,'--component1',$c1,'--plane1','front','--component2',$c2,'--plane2','front','--distance','25') | Out-Null
    Run @('add-mate-coincident','--assembly',$asm,'--component1',$c1,'--plane1','top','--component2',$c2,'--plane2','top') | Out-Null

    $d = (Run @('inspect-assembly','--input',$asm)).data
    $distName = (Mate $d 'distance').name
    Check "distance starts at 25" ((Mate $d 'distance').value -eq 25) "got $((Mate $d 'distance').value)"

    $m = Run @('modify-mate','--assembly',$asm,'--mate',$distName,'--value','40')
    Check "modify reports distance change" ($m.message -match 'distance') $m.message
    $d2 = (Run @('inspect-assembly','--input',$asm)).data
    Check "distance now 40 (rebuilt + saved)" ((Mate $d2 'distance').value -eq 40) "got $((Mate $d2 'distance').value)"

    $coinName = (Mate $d 'coincident').name
    $bad1 = TryRun @('modify-mate','--assembly',$asm,'--mate',$coinName,'--value','5')
    Check "coincident modify rejected (non-zero)" ($bad1.Code -ne 0) "code=$($bad1.Code)"
    Check "coincident error mentions editable value" ($bad1.Out -match 'editable value') $bad1.Out

    $bad2 = TryRun @('modify-mate','--assembly',$asm,'--mate','NoSuchMate','--value','5')
    Check "non-existent mate rejected (non-zero)" ($bad2.Code -ne 0) "code=$($bad2.Code)"
    Check "error mentions cannot find" ($bad2.Out -match 'find') $bad2.Out

    Write-Host "== B) angle mate 30 -> 45 deg =="
    Run @('new-assembly','--out',$asmB) | Out-Null
    Run @('add-component','--assembly',$asmB,'--component',$cyl) | Out-Null
    Run @('add-component','--assembly',$asmB,'--component',$cyl) | Out-Null
    $nb = Names $asmB
    Run @('add-mate-angle','--assembly',$asmB,'--component1',$nb[0],'--plane1','front','--component2',$nb[1],'--plane2','front','--angle','30') | Out-Null
    $db = (Run @('inspect-assembly','--input',$asmB)).data
    $angName = (Mate $db 'angle').name
    Check "angle starts at 30 deg" (((Mate $db 'angle').value -eq 30) -and ((Mate $db 'angle').unit -eq 'deg')) "got $((Mate $db 'angle').value)$((Mate $db 'angle').unit)"
    Run @('modify-mate','--assembly',$asmB,'--mate',$angName,'--value','45') | Out-Null
    $db2 = (Run @('inspect-assembly','--input',$asmB)).data
    Check "angle now 45 deg" (((Mate $db2 'angle').value -eq 45) -and ((Mate $db2 'angle').unit -eq 'deg')) "got $((Mate $db2 'angle').value)$((Mate $db2 'angle').unit)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M42 modify-mate -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($p in @($asm, $asmB, $cyl)) { if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue } }
}
