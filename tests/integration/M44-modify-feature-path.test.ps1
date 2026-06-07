# L2 integration: M44 — modify_feature FILE mode (--part): edit a SAVED part
# file's primary dimension + rebuild + save, without it being the active doc.
# This is the part-side write primitive assembly resize needs (the active-doc
# mode is M38; the mate side is modify_mate).
#
#   in-place: cylinder L60 -> --part edit to 90 -> inspect_part reads 90 (bbox z too)
#   copy:     --part + --out -> copy is 50, original stays 90
#   negatives: missing part file; unknown feature.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M44-modify-feature-path.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cyl = Join-Path $tmpDir ("m44_cyl_{0}.sldprt" -f $rand)
$cylCopy = Join-Path $tmpDir ("m44_copy_{0}.sldprt" -f $rand)

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
function ExtName($d) { return (@($d.features | Where-Object { $_.typeName -eq 'Extrusion' }))[0].name }
function ExtDimVal($d) {
    $f = (@($d.features | Where-Object { $_.typeName -eq 'Extrusion' }))[0]
    return (@($f.dimensions))[0].value
}

try {
    Write-Host "== file mode: edit a saved cylinder's length 60 -> 90 in place =="
    Run @('create-cylinder','--diameter','40','--length','60','--out',$cyl) | Out-Null
    $d0 = (Run @('inspect-part','--input',$cyl)).data
    $ext = ExtName $d0
    Check "starts at L60" ((ExtDimVal $d0) -eq 60) "got $(ExtDimVal $d0)"

    $m = Run @('modify-feature','--part',$cyl,'--feature',$ext,'--value','90')
    Check "reports saved in place" ($m.message -match 'in place') $m.message
    $d1 = (Run @('inspect-part','--input',$cyl)).data
    Check "in-place: dim now 90" ((ExtDimVal $d1) -eq 90) "got $(ExtDimVal $d1)"
    Check "in-place: bbox z now 90 (rebuilt)" ($d1.sizeMm.z -eq 90) "got $($d1.sizeMm.z)"

    Write-Host "== file mode + --out: write a copy at 50, original stays 90 =="
    $m2 = Run @('modify-feature','--part',$cyl,'--feature',$ext,'--value','50','--out',$cylCopy)
    Check "reports saved as a copy" ($m2.message -match 'copy') $m2.message
    Check "copy is L50" ((ExtDimVal (Run @('inspect-part','--input',$cylCopy)).data) -eq 50) ""
    Check "original unchanged (still 90)" ((ExtDimVal (Run @('inspect-part','--input',$cyl)).data) -eq 90) ""

    Write-Host "== negatives =="
    $bad1 = TryRun @('modify-feature','--part',(Join-Path $tmpDir ("nope_{0}.sldprt" -f $rand)),'--feature',$ext,'--value','5')
    Check "missing part file rejected" ($bad1.Code -ne 0) "code=$($bad1.Code)"
    $bad2 = TryRun @('modify-feature','--part',$cyl,'--feature','NoSuchFeature','--value','5')
    Check "unknown feature rejected" ($bad2.Code -ne 0) "code=$($bad2.Code)"
    Check "error mentions cannot find" ($bad2.Out -match 'find') $bad2.Out

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M44 modify-feature --part -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($cyl, $cylCopy)) { if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }
}
