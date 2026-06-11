# L2 integration: M51 — inspect_topology (per-face / per-edge deep inspection).
#
#   Test 1  cylinder D40 L30 (FILE mode --part): 3 faces = 2 planes (normal
#           +/-Z, area ~ pi*20^2) + 1 cylinder (radius 20, axis Z, area
#           ~ pi*40*30); 2 circle edges r20 length ~ 2*pi*20.
#   Test 2  block 30x20x10 (ACTIVE mode): 6 planes with paired areas
#           600/300/200, 12 line edges totalling 240 mm, endpoints present.
#   Test 3  negative: missing part file rejected.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M51-inspect-topology.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$cyl = Join-Path $tmpDir ("m51_cyl_{0}.sldprt" -f $rand)
$blk = Join-Path $tmpDir ("m51_blk_{0}.sldprt" -f $rand)

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
function Near($a, $b, $tol = 0.5) { return [Math]::Abs([double]$a - [double]$b) -lt $tol }

try {
    # ═══ 1. Cylinder D40 L30 — FILE mode ════════════════════════════════════
    Write-Host "== cylinder D40 L30 topology (--part) =="
    Run @('create-cylinder','--diameter','40','--length','30','--out',$cyl) | Out-Null
    $t = (Run @('inspect-topology','--part',$cyl)).data
    Check "cylinder: 3 faces / 2 edges" (($t.faceCount -eq 3) -and ($t.edgeCount -eq 2)) "f=$($t.faceCount) e=$($t.edgeCount)"

    $planes = @($t.faces | Where-Object { $_.type -eq 'plane' })
    $cyls   = @($t.faces | Where-Object { $_.type -eq 'cylinder' })
    Check "cylinder: 2 plane + 1 cylinder faces" (($planes.Count -eq 2) -and ($cyls.Count -eq 1)) `
        (($t.faces | ForEach-Object { $_.type }) -join ',')
    Check "plane normals are +/-Z" `
        ((@($planes | Where-Object { Near $_.normal.z 1 0.01 }).Count -eq 1) -and
         (@($planes | Where-Object { Near $_.normal.z -1 0.01 }).Count -eq 1)) ''
    Check "plane area ~ 1256.6 mm2 (pi*r^2)" (Near $planes[0].areaMm2 1256.64 2) "got $($planes[0].areaMm2)"
    Check "cylinder face: radius 20, area ~ 3769.9 (pi*D*L)" `
        ((Near $cyls[0].radiusMm 20 0.01) -and (Near $cyls[0].areaMm2 3769.91 2)) `
        "r=$($cyls[0].radiusMm) a=$($cyls[0].areaMm2)"
    Check "cylinder axis along Z" (Near ([Math]::Abs($cyls[0].axisDir.z)) 1 0.01) "axisZ=$($cyls[0].axisDir.z)"

    $circles = @($t.edges | Where-Object { $_.type -eq 'circle' })
    Check "both edges are circles r20" `
        (($circles.Count -eq 2) -and (@($circles | Where-Object { Near $_.radiusMm 20 0.01 }).Count -eq 2)) `
        (($t.edges | ForEach-Object { $_.type }) -join ',')
    Check "circle edge length ~ 125.66 (2*pi*r)" (Near $circles[0].lengthMm 125.66 0.5) "got $($circles[0].lengthMm)"

    # ═══ 2. Block 30x20x10 — ACTIVE mode ════════════════════════════════════
    Write-Host "== block 30x20x10 topology (active) =="
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-rectangle-center','--cx','0','--cy','0','--corner-x','15','--corner-y','10') | Out-Null
    $endMsg = (Run @('end-sketch')).message
    if ($endMsg -notmatch "sketch name='([^']+)'") { throw "no sketch name: $endMsg" }
    Run @('extrude','--sketch',$Matches[1],'--depth','10') | Out-Null
    $b = (Run @('inspect-topology')).data
    Check "block: 6 faces / 12 edges, all planes/lines" `
        (($b.faceCount -eq 6) -and ($b.edgeCount -eq 12) -and
         (@($b.faces | Where-Object { $_.type -eq 'plane' }).Count -eq 6) -and
         (@($b.edges | Where-Object { $_.type -eq 'line' }).Count -eq 12)) `
        "f=$($b.faceCount) e=$($b.edgeCount)"
    foreach ($pair in @(@(600, 2), @(300, 2), @(200, 2))) {
        $n = @($b.faces | Where-Object { Near $_.areaMm2 $pair[0] 1 }).Count
        Check "block: 2 faces of $($pair[0]) mm2" ($n -eq $pair[1]) "got $n"
    }
    $totalLen = ($b.edges | Measure-Object -Property lengthMm -Sum).Sum
    Check "block: total edge length 240 mm" (Near $totalLen 240 1) "got $totalLen"
    Check "block: line edges carry endpoints" ($null -ne $b.edges[0].startMm -and $null -ne $b.edges[0].endMm) ''
    Run @('save-part','--out',$blk) | Out-Null

    # ═══ 3. negative ════════════════════════════════════════════════════════
    $bad = TryRun @('inspect-topology','--part',(Join-Path $tmpDir 'no_such.sldprt'))
    Check "missing part file exits non-zero" ($bad.Code -ne 0) "code=$($bad.Code)"

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M51 inspect-topology -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($cyl, $blk)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
