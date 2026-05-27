# L2 integration: ping subcommand should print "pong" and exit 0.
# Run: pwsh ./tests/integration/M1-ping.test.ps1
#      (or via run-all.ps1)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

# ── text mode ─────────────────────────────────────────────────────────────
$out = & $exe ping
if ($LASTEXITCODE -ne 0) { throw "ping exited $LASTEXITCODE" }
if ($out -ne 'pong')    { throw "expected 'pong', got '$out'" }

# ── json mode ─────────────────────────────────────────────────────────────
$json = & $exe ping --output json
if ($LASTEXITCODE -ne 0) { throw "ping --output json exited $LASTEXITCODE" }
$obj = $json | ConvertFrom-Json
if ($obj.status -ne 'ok')      { throw "json status: '$($obj.status)'" }
if ($obj.message -ne 'pong')   { throw "json message: '$($obj.message)'" }

Write-Host '[ok] M1-ping (text + json)'
