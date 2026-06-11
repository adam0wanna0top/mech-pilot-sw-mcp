# L2 integration: M48 — delete_feature + suppress_feature (the mechanical-
# Cursor rollback primitives).
#
# Geometry round trip on a base(D40x30) + boss(D20x10 on the +z face) part:
#   ACTIVE mode: suppress boss -> bbox z 40->30 + suppressed=true ->
#                unsuppress -> z back to 40.
#   FILE mode:   suppress/unsuppress/delete the boss in the SAVED file via
#                --part (bbox + featureCount verified by inspect-part).
#   Negatives:   unknown feature name -> friendly; deleting a default plane
#                (前视基准面) -> refused (boot-geometry guard).
#   ACTIVE delete: fresh part, delete the boss live -> features drop 4->2.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M48-feature-management.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$part = Join-Path $tmpDir ("m48_part_{0}.sldprt" -f $rand)
$discard = Join-Path $tmpDir ("m48_discard_{0}.sldprt" -f $rand)

$script:fail = 0
function Check([string]$label, [bool]$cond, [string]$detail = '') {
    if ($cond) { Write-Host "[ok] $label" }
    else { Write-Host "[FAIL] $label $detail"; $script:fail++ }
}
function Run([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    $raw = ($o -join "`n")
    if ($LASTEXITCODE -ne 0) { throw "command failed: $($a -join ' ')`n$raw" }
    return $raw | ConvertFrom-Json
}
function TryRun([string[]]$a) {
    $o = & $exe @a --output json 2>&1
    return [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($o -join "`n") }
}
function SK($obj) { if ($obj.message -match "sketch name='([^']+)'") { return $Matches[1] }; throw "no sketch name: $($obj.message)" }
function BossState($info) {
    $boss = $info.data.features | Where-Object { $_.name -eq '凸台-拉伸2' } | Select-Object -First 1
    return $boss
}

try {
    # ── Setup: base D40x30 + boss D20x10 on the top (+z) face ───────────────
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s1 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s1,'--depth','30') | Out-Null
    Run @('start-sketch','--plane','+z') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','10') | Out-Null
    $s2 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s2,'--depth','10') | Out-Null
    $i0 = Run @('inspect-active')
    Check "setup: bbox z=40 (base 30 + boss 10)" ([Math]::Abs($i0.data.sizeMm.z - 40) -lt 0.1) "z=$($i0.data.sizeMm.z)"
    Check "setup: 4 features" ($i0.data.featureCount -eq 4) "fc=$($i0.data.featureCount)"

    # ── ACTIVE mode: suppress -> geometry drops, tree keeps the feature ─────
    Write-Host "== ACTIVE suppress / unsuppress =="
    Run @('suppress-feature','--feature','凸台-拉伸2') | Out-Null
    $i1 = Run @('inspect-active')
    Check "suppress: bbox z back to 30" ([Math]::Abs($i1.data.sizeMm.z - 30) -lt 0.1) "z=$($i1.data.sizeMm.z)"
    Check "suppress: feature still in tree, suppressed=true" ((BossState $i1).suppressed -eq $true) (($i1.data.features | % name) -join ',')
    Run @('suppress-feature','--feature','凸台-拉伸2','--unsuppress') | Out-Null
    $i2 = Run @('inspect-active')
    Check "unsuppress: bbox z restored to 40" ([Math]::Abs($i2.data.sizeMm.z - 40) -lt 0.1) "z=$($i2.data.sizeMm.z)"
    Check "unsuppress: suppressed=false" ((BossState $i2).suppressed -eq $false) ''
    Run @('save-part','--out',$part) | Out-Null

    # ── FILE mode: suppress / unsuppress / delete in the saved file ─────────
    Write-Host "== FILE mode on saved part =="
    Run @('suppress-feature','--feature','凸台-拉伸2','--part',$part) | Out-Null
    $f1 = Run @('inspect-part','--input',$part)
    Check "file suppress: bbox z=30 + suppressed persisted" `
        (([Math]::Abs($f1.data.sizeMm.z - 30) -lt 0.1) -and ((BossState $f1).suppressed -eq $true)) "z=$($f1.data.sizeMm.z)"
    Run @('suppress-feature','--feature','凸台-拉伸2','--unsuppress','--part',$part) | Out-Null
    $f2 = Run @('inspect-part','--input',$part)
    Check "file unsuppress: bbox z=40" ([Math]::Abs($f2.data.sizeMm.z - 40) -lt 0.1) "z=$($f2.data.sizeMm.z)"
    Run @('delete-feature','--feature','凸台-拉伸2','--part',$part) | Out-Null
    $f3 = Run @('inspect-part','--input',$part)
    Check "file delete: features 4 -> 2 (boss + absorbed sketch gone)" ($f3.data.featureCount -eq 2) "fc=$($f3.data.featureCount)"
    Check "file delete: bbox z=30" ([Math]::Abs($f3.data.sizeMm.z - 30) -lt 0.1) "z=$($f3.data.sizeMm.z)"

    # ── Negatives (FILE mode on the saved part) ─────────────────────────────
    Write-Host "== negatives =="
    $bad1 = TryRun @('delete-feature','--feature','no-such-feature','--part',$part)
    Check "unknown feature exits non-zero" ($bad1.Code -ne 0) "code=$($bad1.Code)"
    Check "unknown feature error guides to inspect" ($bad1.Out -match 'No feature named') $bad1.Out
    $bad2 = TryRun @('delete-feature','--feature','前视基准面','--part',$part)
    Check "default plane delete exits non-zero" ($bad2.Code -ne 0) "code=$($bad2.Code)"
    Check "default plane delete is refused (boot guard)" ($bad2.Out -match 'Refusing') $bad2.Out

    # ── ACTIVE delete: fresh part, drop the boss live ───────────────────────
    Write-Host "== ACTIVE delete =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','20') | Out-Null
    $s3 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s3,'--depth','20') | Out-Null
    Run @('start-sketch','--plane','+z') | Out-Null
    Run @('sketch-circle','--cx','0','--cy','0','--radius','8') | Out-Null
    $s4 = SK (Run @('end-sketch'))
    Run @('extrude','--sketch',$s4,'--depth','8') | Out-Null
    Run @('delete-feature','--feature','凸台-拉伸2') | Out-Null
    $a1 = Run @('inspect-active')
    Check "active delete: features 4 -> 2" ($a1.data.featureCount -eq 2) "fc=$($a1.data.featureCount)"
    Check "active delete: bbox z=20" ([Math]::Abs($a1.data.sizeMm.z - 20) -lt 0.1) "z=$($a1.data.sizeMm.z)"
    Run @('save-part','--out',$discard) | Out-Null

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M48 feature management -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($part, $discard)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
