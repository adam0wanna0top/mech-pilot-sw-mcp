# L2 integration: ping subcommand prints "pong" + the build identity (M57:
# git SHA + build time) and exits 0, in text and json modes.
# Run: pwsh ./tests/integration/M1-ping.test.ps1
#      (or via run-all.ps1)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

# ── text mode: "pong — git <sha>, built <time>" ─────────────────────────────
$out = & $exe ping
if ($LASTEXITCODE -ne 0) { throw "ping exited $LASTEXITCODE" }
if ($out -notmatch '^pong')  { throw "expected message to start with 'pong', got '$out'" }
if ($out -notmatch 'git ')   { throw "expected build git info in '$out'" }
if ($out -notmatch 'built ') { throw "expected build time in '$out'" }

# ── json mode: structured build data ────────────────────────────────────────
$json = & $exe ping --output json
if ($LASTEXITCODE -ne 0) { throw "ping --output json exited $LASTEXITCODE" }
$obj = $json | ConvertFrom-Json
if ($obj.status -ne 'ok')          { throw "json status: '$($obj.status)'" }
if ($obj.message -notmatch '^pong') { throw "json message: '$($obj.message)'" }
if ($null -eq $obj.data)           { throw "json data block missing" }
if ($null -eq $obj.data.gitSha)    { throw "data.gitSha missing" }
if ($null -eq $obj.data.buildTimeUtc) { throw "data.buildTimeUtc missing" }
# gitDirty is a bool; PSObject surfaces it as $true/$false
if ($obj.data.PSObject.Properties.Name -notcontains 'gitDirty') { throw "data.gitDirty missing" }

Write-Host ("[ok] M1-ping (text + json): {0}" -f $obj.message)
