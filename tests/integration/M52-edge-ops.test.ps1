# L2 integration: M52 — fillet_edges / chamfer_edges (precise, topology-addressed).
#
#   Test 1  ACTIVE fillet: block 30x20x10 -> inspect-topology -> pick ONE
#           vertical edge (line, 10 mm) BY SIGNATURE -> fillet r3 ->
#           re-inspect: 7 faces, exactly 1 cylinder r3 (the other 11 edges
#           untouched — the whole point vs add_fillet).
#   Test 2  ACTIVE chamfer: fresh block -> chamfer one vertical edge d2 ->
#           7 faces, ALL planes (chamfer face is flat), 0 cylinders.
#   Test 3  FILE mode: create-rectangular-block -> fillet all 4 vertical
#           edges via --part -> topo --part: 4 cylinder faces r2.
#   Test 4  negative: edge index 99 -> friendly out-of-range with the valid
#           range and an inspect_topology pointer.
#
# Requires SolidWorks. Run:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests/integration/M52-edge-ops.test.ps1

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exe = Join-Path $PSScriptRoot '..\..\MechPilot.SwMcp\bin\Debug\net8.0-windows\mech-pilot-sw.exe'
if (-not (Test-Path $exe)) { throw "exe not found: $exe (run 'dotnet build' first)" }

$tmpDir = Join-Path $env:TEMP 'mech-pilot-sw-test'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$rand = Get-Random
$blkA = Join-Path $tmpDir ("m52_a_{0}.sldprt" -f $rand)
$blkB = Join-Path $tmpDir ("m52_b_{0}.sldprt" -f $rand)
$blkC = Join-Path $tmpDir ("m52_c_{0}.sldprt" -f $rand)

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
function NewBlock() {
    Run @('new-part') | Out-Null
    Run @('start-sketch','--plane','front') | Out-Null
    Run @('sketch-rectangle-center','--cx','0','--cy','0','--corner-x','15','--corner-y','10') | Out-Null
    $m = (Run @('end-sketch')).message
    if ($m -notmatch "sketch name='([^']+)'") { throw "no sketch name: $m" }
    Run @('extrude','--sketch',$Matches[1],'--depth','10') | Out-Null
}
function VerticalEdgeIndexes($topo) {
    return @($topo.edges | Where-Object { $_.type -eq 'line' -and (Near $_.lengthMm 10 0.1) } |
             ForEach-Object { $_.index })
}

try {
    # ═══ 1. ACTIVE fillet — one edge only ═══════════════════════════════════
    Write-Host "== 1: fillet ONE vertical edge of a block (active) =="
    NewBlock
    $t0 = (Run @('inspect-topology')).data
    $vert = VerticalEdgeIndexes $t0
    Check "block has 4 vertical 10mm edges" ($vert.Count -eq 4) "got $($vert.Count)"
    $pick = $vert[0]
    $f = Run @('fillet-edges','--edges',"$pick",'--radius','3')
    Check "fillet message echoes the edge signature" ($f.message -match "#$pick line 10 mm") $f.message
    $t1 = (Run @('inspect-topology')).data
    $cyls = @($t1.faces | Where-Object { $_.type -eq 'cylinder' })
    Check "after fillet: 7 faces, exactly 1 cylinder" (($t1.faceCount -eq 7) -and ($cyls.Count -eq 1)) `
        "f=$($t1.faceCount) cyl=$($cyls.Count)"
    Check "fillet cylinder radius = 3" (Near $cyls[0].radiusMm 3 0.01) "r=$($cyls[0].radiusMm)"
    # 3 untouched verticals + 2 NEW 10mm tangent seam lines where the fillet
    # cylinder meets the side planes = 5 vertical 10mm lines after the fillet.
    Check "other verticals untouched (3 + 2 fillet seams = 5)" ((VerticalEdgeIndexes $t1).Count -eq 5) `
        "got $((VerticalEdgeIndexes $t1).Count)"
    Run @('save-part','--out',$blkA) | Out-Null

    # ═══ 2. ACTIVE chamfer — one edge ═══════════════════════════════════════
    Write-Host "== 2: chamfer ONE vertical edge (active) =="
    NewBlock
    $t2 = (Run @('inspect-topology')).data
    $pick2 = (VerticalEdgeIndexes $t2)[0]
    $c = Run @('chamfer-edges','--edges',"$pick2",'--distance','2')
    Check "chamfer message echoes the edge signature" ($c.message -match "#$pick2 line 10 mm") $c.message
    $t3 = (Run @('inspect-topology')).data
    Check "after chamfer: 7 faces, all planes (chamfer face is flat)" `
        (($t3.faceCount -eq 7) -and (@($t3.faces | Where-Object { $_.type -eq 'plane' }).Count -eq 7)) `
        "f=$($t3.faceCount)"
    Run @('save-part','--out',$blkB) | Out-Null

    # ═══ 3. FILE mode — fillet all 4 vertical edges of a catalog block ══════
    Write-Host "== 3: fillet 4 vertical edges via --part (file mode) =="
    Run @('create-rectangular-block','--length','30','--width','20','--height','10','--out',$blkC) | Out-Null
    $t4 = (Run @('inspect-topology','--part',$blkC)).data
    $vert4 = VerticalEdgeIndexes $t4
    Check "catalog block has 4 vertical edges" ($vert4.Count -eq 4) "got $($vert4.Count)"
    $edgeArgs = @('fillet-edges','--edges') + ($vert4 | ForEach-Object { "$_" }) + @('--radius','2','--part',$blkC)
    $f4 = Run $edgeArgs
    Check "file-mode fillet saved" ($f4.message -match 'saved') $f4.message
    $t5 = (Run @('inspect-topology','--part',$blkC)).data
    $cyls4 = @($t5.faces | Where-Object { $_.type -eq 'cylinder' })
    Check "file mode: 4 cylinder faces r2" `
        (($cyls4.Count -eq 4) -and (@($cyls4 | Where-Object { Near $_.radiusMm 2 0.01 }).Count -eq 4)) `
        "cyl=$($cyls4.Count)"

    # ═══ 4. negative: out-of-range index ════════════════════════════════════
    Write-Host "== 4: negative =="
    NewBlock
    $bad = TryRun @('fillet-edges','--edges','99','--radius','3')
    Check "index 99 exits non-zero" ($bad.Code -ne 0) "code=$($bad.Code)"
    Check "error names the valid range + inspect_topology" `
        (($bad.Out -match '0\.\.11') -and ($bad.Out -match 'inspect_topology')) $bad.Out
    $discard = Join-Path $tmpDir ("m52_discard_{0}.sldprt" -f $rand)
    Run @('save-part','--out',$discard) | Out-Null
    if (Test-Path $discard) { Remove-Item $discard -Force -EA SilentlyContinue }

    Write-Host ""
    if ($script:fail -eq 0) { Write-Host "[PASS] M52 edge ops -- all checks green" }
    else { Write-Host "[FAILED] $($script:fail) check(s) failed"; exit 1 }
} finally {
    foreach ($f in @($blkA, $blkB, $blkC)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
}
