<#
.SYNOPSIS
Captures one stage of the base from a save into src\Base\Growth.json.

.DESCRIPTION
A stage is what you changed in the base when an item unlocked. The save is compared with what the base should be before this stage: the T0 template plus every other
stage already in Growth.json. What differs becomes entries that carry -Needs (the group whose unlock triggers the stage):

  add     a piece in the save that the earlier base has no match for (foundations, pods, food growers with their seeds, moved furniture at its new place, ...)
  remove  a piece of the earlier base that the save no longer has (the bed, furniture at its old place)
  (none)  a pod whose panels changed (a window or door added to an existing pod). "from" is the panels it must still have, so a pod the player changed is left alone.

Entries already in Growth.json with the same -Needs are replaced, so a stage can be captured again. Things that are NOT captured: the energy generators (the power units
stay in T0; /power on|off switches them), drills (the player's own), the plants that grow in a food grower (the game makes them from the seed), the escape pod, and anything
without a position. -LabelOnly names groups that keep just their label (a crate: no linked group, no contents). -Only and -Except limit a capture to (or keep it from) the named groups, to split one save into stages by what unlocked when. -Include names groups that are normally left out (for example ScreenTerraStage) so that one can be captured as a stage of its own. Upgrades (Heater1 -> Heater2, Vegetube1 -> Vegetube2, lamp -> beacon) count as the same piece: the Base mod's upgrade chains do those.

A stage captured out of order: -SkipNeeds names the stages of Growth.json the save has not reached yet (their pieces are not in the save, so they stay out of the earlier base it is compared with and do not show up as removes). Their entries are kept in the file.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\capture-growth.ps1 -Save "$env:USERPROFILE\AppData\LocalLow\MijuGames\Planet Crafter\Custom-1.json" -Needs VegetableGrower1
#>
param(
    [Parameter(Mandatory)] [string] $Save,
    [Parameter(Mandatory)] [string] $Needs,
    [string] $Out,
    [string[]] $Include = @(),
    [string[]] $Only = @(),
    [string[]] $Except = @(),
    [string[]] $LabelOnly = @(),
    [string[]] $SkipNeeds = @(),
    [switch] $DryRun
)
$ErrorActionPreference = 'Stop'
# a list given with -File arrives as one comma separated text
foreach ($name in 'Include', 'Only', 'Except', 'LabelOnly', 'SkipNeeds') { Set-Variable -Name $name -Value @((Get-Variable -Name $name -ValueOnly) | ForEach-Object { $_ -split ',' } | Where-Object { $_ }) }
$inv = [Globalization.CultureInfo]::InvariantCulture
$root = Split-Path -Parent $PSScriptRoot
$t0Path = Join-Path $root 'src\Base\T0.json'
if (-not $Out) { $Out = Join-Path $root 'src\Base\Growth.json' }

# panel type -> the group that has to be unlocked before a pod may use it (3 = the living compartment window)
$PanelNeeds = @{ 3 = 'window' }
# groups the capture leaves alone
$Ignore = @('EscapePod', 'ScreenTerraStage') | Where-Object { $Include -notcontains $_ }
$IgnorePattern = '^(EnergyGenerator\d*|Drill\d+|Vegetable\dGrowable)$'

function Family([string] $g) {
    if ($g -match '^EnergyGenerator') { return 'EnergyGenerator' }
    if ($g -match '^Heater\d') { return 'Heater' }
    if ($g -match '^Vegetube\d') { return 'Vegetube' }
    if ($g -match '^Drill\d') { return 'Drill' }
    if ($g -eq 'OutsideLamp1' -or $g -eq 'Beacon') { return 'Lamp' }
    return $g
}
function Dist($a, $b) { [math]::Sqrt([math]::Pow($a[0] - $b[0], 2) + [math]::Pow($a[1] - $b[1], 2) + [math]::Pow($a[2] - $b[2], 2)) }
function Num([string] $s) { [double]::Parse($s, $inv) }

# ---- the save ---------------------------------------------------------------------------------------------------------------
$text = [IO.File]::ReadAllText($Save)
$records = foreach ($m in [regex]::Matches($text, '\{"id":\d+,"gId":"[^"]+"[^{}]*\}')) { try { $m.Value | ConvertFrom-Json } catch { } }
$byId = @{}; foreach ($r in $records) { $byId[[int64]$r.id] = $r.gId }
$inventories = @{}
foreach ($m in [regex]::Matches($text, '\{"id":(\d+),"woIds":"([^"]*)","size":\d+\}')) {
    $inventories[[int]$m.Groups[1].Value] = @($m.Groups[2].Value -split ',' | Where-Object { $_ } | ForEach-Object { $byId[[int64]$_] } | Where-Object { $_ })
}

$t0 = Get-Content $t0Path -Raw | ConvertFrom-Json
$anchor = @([double]$t0.anchor[0], [double]$t0.anchor[1], [double]$t0.anchor[2])

$placed = @($records | Where-Object { $_.pos -and $Ignore -notcontains $_.gId -and $_.gId -notmatch $IgnorePattern })
$saveItems = New-Object System.Collections.ArrayList
foreach ($r in $placed) {
    $p = $r.pos -split ','
    $item = [ordered]@{
        g = $r.gId; fam = (Family $r.gId)
        x = [math]::Round((Num $p[0]) - $anchor[0], 5); y = [math]::Round((Num $p[1]) - $anchor[1], 5); z = [math]::Round((Num $p[2]) - $anchor[2], 5)
        rot = if ($r.rot) { $r.rot } else { '0,0,0,1' }
        panels = [string]$r.pnls; text = [string]$r.text; color = [string]$r.color; groups = [string]$r.liGrps; fill = ''; claimed = $false
    }
    if ($r.liId -and $r.gId -notlike 'Container*' -and $inventories.ContainsKey([int]$r.liId)) { $item.fill = ($inventories[[int]$r.liId] -join ',') }
    [void]$saveItems.Add($item)
}
$foundation = $saveItems | Where-Object { $_.g -eq 'Foundation' -and (Dist @($_.x, $_.y, $_.z) @(0, 0, 0)) -lt 0.05 }
if (-not $foundation) { throw "The save has no foundation at the T0 anchor $($anchor -join ','): this is not the Prime base." }

# ---- what the base should be before this stage: T0 plus the other stages ---------------------------------------------------------
function ToItem($p) {
    [ordered]@{ g = $p.g; fam = (Family $p.g); x = [double]$p.x; y = [double]$p.y; z = [double]$p.z; rot = [string]$p.rot; panels = [string]$p.panels
        text = [string]$p.text; color = [string]$p.color; groups = [string]$p.groups; fill = [string]$p.fill; claimed = $false }
}
$before = New-Object System.Collections.ArrayList
foreach ($p in $t0.pieces) { if ($p.g -notmatch $IgnorePattern -and $Ignore -notcontains $p.g) { [void]$before.Add((ToItem $p)) } }

$keep = @()
if (Test-Path $Out) {
    $old = Get-Content $Out -Raw | ConvertFrom-Json
    $keep = @($old.pieces | Where-Object { $_.needs -ne $Needs })
    $keepBefore = @($keep | Where-Object { $SkipNeeds -notcontains $_.needs })   # what the base looked like before this stage: the stages the save has reached
    # Every stage's adds first, then the removes, then the pod panel changes, whatever order the stages sit in the file: a later stage may remove or change what an earlier one
    # added, and a stage captured again lands at the end of the file.
    foreach ($e in ($keepBefore | Where-Object { $_.op -eq 'add' -and $Ignore -notcontains $_.g })) { [void]$before.Add((ToItem $e)) }
    foreach ($e in ($keepBefore | Where-Object { $_.op -eq 'remove' })) {
        $gone = $before | Where-Object { $_.g -eq $e.g -and (Dist @($_.x, $_.y, $_.z) @($e.x, $e.y, $e.z)) -lt 0.3 } | Select-Object -First 1
        if ($gone) { $before.Remove($gone) }
    }
    foreach ($e in ($keepBefore | Where-Object { -not $_.op })) {
        $pod = $before | Where-Object { $_.g -eq $e.g -and (Dist @($_.x, $_.y, $_.z) @($e.x, $e.y, $e.z)) -lt 0.05 } | Select-Object -First 1
        if ($pod) { $pod.panels = [string]$e.panels }
    }
}

# ---- match ----------------------------------------------------------------------------------------------------------------
$entries = New-Object System.Collections.ArrayList
$tolerance = 0.3
foreach ($b in $before) {
    $hit = $saveItems | Where-Object { -not $_.claimed -and $_.fam -eq $b.fam -and (Dist @($_.x, $_.y, $_.z) @($b.x, $b.y, $b.z)) -lt $tolerance } |
        Sort-Object { Dist @($_.x, $_.y, $_.z) @($b.x, $b.y, $b.z) } | Select-Object -First 1
    if ($hit) {
        $hit.claimed = $true
        $b.claimed = $true
        if ($b.g -eq 'pod' -and $hit.panels -ne $b.panels) {
            $oldP = @($b.panels -split ',' | ForEach-Object { [int]$_ }); $newP = @($hit.panels -split ',' | ForEach-Object { [int]$_ })
            $changed = @(0..($oldP.Count - 1) | Where-Object { $oldP[$_] -ne $newP[$_] } | ForEach-Object { $newP[$_] } | Sort-Object -Unique)
            $needsOf = @($changed | ForEach-Object { if ($PanelNeeds.ContainsKey($_)) { $PanelNeeds[$_] } else { $Needs } } | Sort-Object -Unique)
            $n = if ($needsOf.Count -eq 1) { $needsOf[0] } else { $Needs }
            [void]$entries.Add([ordered]@{ g = 'pod'; x = $b.x; y = $b.y; z = $b.z; rot = $b.rot; from = $b.panels; panels = $hit.panels; needs = $n })
            Write-Host ("pod at x {0} z {1}: panels {2} -> {3} (needs {4})" -f $b.x, $b.z, $b.panels, $hit.panels, $n)
        }
    }
}
$removes = @($before | Where-Object { -not $_.claimed -and ($Only.Count -eq 0 -or $Only -contains $_.g) -and $Except -notcontains $_.g })
$adds = @($saveItems | Where-Object { -not $_.claimed -and ($Only.Count -eq 0 -or $Only -contains $_.g) -and $Except -notcontains $_.g })

foreach ($b in $removes) {
    [void]$entries.Add([ordered]@{ g = $b.g; x = $b.x; y = $b.y; z = $b.z; rot = $b.rot; op = 'remove'; needs = $Needs })
    Write-Host ("remove {0} at x {1} y {2} z {3}" -f $b.g, $b.x, $b.y, $b.z)
}
$order = @{ Foundation = 0; pod = 1 }
foreach ($a in ($adds | Sort-Object { if ($order.ContainsKey($_.g)) { $order[$_.g] } else { 2 } }, { $_.x }, { $_.z }, { $_.y })) {
    $e = [ordered]@{ g = $a.g; x = $a.x; y = $a.y; z = $a.z; rot = $a.rot }
    if ($a.text) { $e.text = $a.text }
    $labelOnly = $LabelOnly -contains $a.g
    if ($a.groups -and -not $labelOnly) { $e.groups = $a.groups }
    if ($a.panels -and -not $labelOnly) { $e.panels = $a.panels }
    if ($a.color -and -not $labelOnly) { $e.color = $a.color }
    if ($a.fill -and -not $labelOnly) { $e.fill = $a.fill }
    $e.op = 'add'; $e.needs = $Needs
    [void]$entries.Add($e)
}
Write-Host ("add: " + (($adds | Group-Object { $_.g } | Sort-Object Count -Descending | ForEach-Object { "$($_.Count) x $($_.Name)" }) -join ', '))

# ---- write ----------------------------------------------------------------------------------------------------------------
$kept = @($keep | ForEach-Object { $ht = [ordered]@{}; $_.PSObject.Properties | ForEach-Object { $ht[$_.Name] = $_.Value }; $ht })
$all = $kept + @($entries)
$result = [ordered]@{ name = 'Growth'; capturedFrom = [IO.Path]::GetFileName($Save); anchor = $anchor; pieces = $all }
$json = $result | ConvertTo-Json -Depth 5
if ($DryRun) { Write-Host "(dry run: $($all.Count) entries, nothing written)"; return }
[IO.File]::WriteAllText($Out, ($json -replace "`r`n", "`n") + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Host ("{0} entries ({1} for {2}) written to {3}" -f $all.Count, $entries.Count, $Needs, $Out)
