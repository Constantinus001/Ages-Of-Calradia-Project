param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\tmp\world-events-shell-repair\bin\Win64_Shipping_Client\AgesOfCalradia.WorldEventsShellRepair.dll')
)

$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$source = Get-Content -LiteralPath (Join-Path $Root 'WorldEventsShellRepairSubModule.cs') -Raw
$project = Get-Content -LiteralPath (Join-Path $Root 'WorldEventsShellRepair.csproj') -Raw
$manifest = Get-Content -LiteralPath (Join-Path $Root 'SubModule.xml') -Raw
$documentation = Get-Content -LiteralPath (Join-Path $Root 'docs\WORLD_EVENTS_SHELL_REPAIR.md') -Raw
$prefabPath = Join-Path $Root 'GUI\Prefabs\WorldCalendar\WorldCalendar.xml'
$rendererPath = Join-Path $Root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'
$skinRoot = Join-Path $Root 'GUI\CustomUI\WorldEventsSkin'

Assert-True ($source -match '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E') 'The sidecar does not pin the approved renderer hash.'
Assert-True ($source -match 'WidgetTemplate' -and $source -match 'PrefabExtensionContext' -and $source -match 'WidgetAttributeContext' -and $source -match 'XmlNode') 'The exact Bannerlord prefab-loader target is incomplete.'
Assert-True ($source -match 'ref XmlNode __2' -and $source -match 'document\.CreateElement\("TextureWidget"\)' -and $source -match 'TextureProviderName') 'The in-memory widget conversion is incomplete.'
Assert-True ($source -match '__2 = originalNode' -and $source -match 'original widget remains active') 'The node conversion does not fail open.'
Assert-True ($source.Contains('alignedFrame.SetAttribute("VerticalAlignment", "Bottom");') -and $source.Contains('alignedFrame.SetAttribute("PositionYOffset", "-131.2");')) 'World Events must bottom-anchor directly above the season crown.'
[xml]$mapLayout=Get-Content -Raw (Join-Path $Root 'GUI/Prefabs/Map/MapBar.xml')
$center=$mapLayout.SelectSingleNode('//*[@Id="CenterPanel"]')
$crown=$mapLayout.SelectSingleNode('//*[@Id="SeasonCrown"]')
$crownInset=0.8*([double]$center.SuggestedHeight-[double]$center.PositionYOffset-[double]$crown.PositionYOffset)
Assert-True ([Math]::Abs((131.2-$crownInset)-4) -lt 0.001) 'World Events bottom edge must clear the season crown by 4 UI units.'
Assert-True ($project -match '<AssemblyName>AgesOfCalradia\.WorldEventsShellRepair</AssemblyName>' -and $project -match 'TaleWorlds\.GauntletUI\.PrefabSystem') 'The standalone UI sidecar project contract is incomplete.'
Assert-True ($manifest -match 'AgesOfCalradia\.WorldEventsShellRepair\.dll' -and $manifest -match 'WorldEventsShellRepairSubModule') 'The shell repair is not registered as a separate submodule.'
Assert-True ($documentation -match 'Harmony target and compatibility' -and $documentation -match 'Failure behavior and diagnostics') 'The native patch compatibility contract is undocumented.'

$mappings = @(
    @('WorldEventsFullBorderShell', 'aoc_world_events_shell_v8', 'WorldEventsFullBorderShellTextureProvider', 'world_events_four_tabs_shell_buttonless_v7_full_bottom_eagle.png'),
    @('WorldEventsSelectedCalendarShell', 'aoc_world_events_shell_calendar_selected_v6', 'WorldEventsGoldFrameCalendarTextureProvider', 'gold_frame_calendar_v4_matte.png'),
    @('WorldEventsSelectedStoryShell', 'aoc_world_events_shell_story_selected_v6', 'WorldEventsGoldFrameStoryTextureProvider', 'gold_frame_story_v4_matte.png'),
    @('WorldEventsSelectedRealmShell', 'aoc_world_events_shell_realm_selected_v6', 'WorldEventsGoldFrameDiplomacyTextureProvider', 'gold_frame_diplomacy_v4_matte.png'),
    @('WorldEventsSelectedStrategicShell', 'aoc_world_events_shell_strategic_selected_v6', 'WorldEventsGoldFrameMapTextureProvider', 'gold_frame_map_v4_matte.png')
)
foreach ($mapping in $mappings) {
    foreach ($token in $mapping[0..2]) {
        Assert-True ($source.Contains($token)) "Sidecar mapping token is missing: $token"
    }
    Assert-True (Test-Path -LiteralPath (Join-Path $skinRoot $mapping[3]) -PathType Leaf) "Direct-provider PNG is missing: $($mapping[3])"
}

$approvedRendererHash = '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E'
$approvedPrefabHash = 'E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E'
Assert-True ((Get-FileHash -LiteralPath $rendererPath -Algorithm SHA256).Hash -eq $approvedRendererHash) 'Repository protected renderer hash changed.'
Assert-True ((Get-FileHash -LiteralPath $prefabPath -Algorithm SHA256).Hash -eq $approvedPrefabHash) 'Repository protected World Events prefab hash changed.'

if (Test-Path -LiteralPath $AssemblyPath -PathType Leaf) {
    $ildasm = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.7.2 Tools\x64\ildasm.exe'
    if (Test-Path -LiteralPath $ildasm) {
        $assemblyText = & $ildasm $AssemblyPath /text
        Assert-True (@($assemblyText | Select-String 'WorldEventsShellRepairSubModule').Count -gt 0) 'Compiled sidecar entry point is missing.'
        Assert-True (@($assemblyText | Select-String 'BeforeWidgetTemplateLoad').Count -gt 0) 'Compiled prefab prefix is missing.'
    }
}

Write-Host 'World Events shell repair verification passed: exact in-memory provider mappings, fail-open behavior, sidecar registration, assets, and protected hashes are valid.'
