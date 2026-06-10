# L2 integration: M47 — insert_toolbox_fastener (Toolbox standard part at a
# chosen size = configuration).
#
# Self-bootstrapping config check (no hard-coded config names — the GB bolt's
# config list belongs to the local Toolbox install):
#   1. Insert with NO config → message reports the default config.
#   2. Insert with a bogus config → tool rejects AND lists available configs
#      (parse-friendly quoted names) → harvest one that differs from default.
#   3. Insert with the harvested config → message reports config='<harvested>'
#      → decisive proof size selection works (≠ default, accepted by SW).
#   4. Missing part path → spec-level rejection.
#
# Requires SolidWorks + Toolbox (GB standard) installed.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M47-toolbox-fastener.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

# GB hex head bolt from the local Toolbox data folder (registry: Toolbox Data Location).
$bolt = 'G:\solidwork\SOLIDWORKS Data2026\browser\GB\bolts and studs\hexagon head bolts\hexagon head bolts gb.sldprt'
if (-not (Test-Path $bolt)) { throw "Toolbox GB bolt not found: $bolt (is Toolbox/GB installed?)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$asm = Join-Path $tmpDir ("m47_tbf_{0}.sldasm" -f $rand)

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

try {
    Run @('new-assembly', '--out', $asm) | Out-Null
    Write-Host "[setup] assembly -> $asm"

    # ── 1. Default-size insert (no --config) ────────────────────────────────
    $r1 = Run @('insert-toolbox-fastener', '--assembly', $asm, '--part', $bolt)
    Check "default insert succeeds" ($r1.message -match 'Inserted Toolbox part') $r1.message
    $defaultCfg = $null
    if ($r1.message -match "config='([^']+)'") { $defaultCfg = $Matches[1] }
    Check "default insert reports a config" ($null -ne $defaultCfg) $r1.message
    Write-Host ("[info] default config = '{0}'" -f $defaultCfg)

    # ── 2. Bogus config rejected + available list harvested ─────────────────
    $bad = TryRun @('insert-toolbox-fastener', '--assembly', $asm, '--part', $bolt, '--config', 'M999X999')
    Check "bogus config exits non-zero" ($bad.Code -ne 0) "code=$($bad.Code)"
    Check "error lists available configurations" ($bad.Out -match 'Available configurations') $bad.Out
    $harvested = [regex]::Matches($bad.Out, "'([^']+)'") |
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ -ne 'M999X999' -and $_ -ne $defaultCfg -and $_ -notmatch '\.sldprt$' } |
        Select-Object -First 1
    Check "harvested a non-default config from the error" ($null -ne $harvested) $bad.Out
    Write-Host ("[info] harvested config = '{0}'" -f $harvested)

    # ── 3. Insert at the harvested size → decisive selection proof ──────────
    $r3 = Run @('insert-toolbox-fastener', '--assembly', $asm, '--part', $bolt,
                '--config', $harvested, '--position-x', '60')
    Check "sized insert succeeds" ($r3.message -match 'Inserted Toolbox part') $r3.message
    Check "sized insert references the requested config (not default)" `
        ($r3.message -match [regex]::Escape("config='$harvested'")) $r3.message

    # ── 4. Missing part path → spec rejection ───────────────────────────────
    $miss = TryRun @('insert-toolbox-fastener', '--assembly', $asm,
                     '--part', (Join-Path $tmpDir 'no_such_bolt.sldprt'))
    Check "missing part path exits non-zero" ($miss.Code -ne 0) "code=$($miss.Code)"
    Check "missing part error mentions Toolbox hint" ($miss.Out -match 'Toolbox') $miss.Out

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M47 insert_toolbox_fastener -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    if (Test-Path $asm) { Remove-Item $asm -Force -ErrorAction SilentlyContinue }
}
