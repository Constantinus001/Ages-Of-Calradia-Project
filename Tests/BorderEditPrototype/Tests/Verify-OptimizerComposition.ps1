param([string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
# Run the real bindings suite in this Windows PowerShell process. Its finally
# removes every patch, so explicitly reinstall the reviewed owners below.
. (Join-Path $PSScriptRoot 'Verify-NativeBindings.ps1') -BannerlordDir $BannerlordDir
function Assert-Composition([bool]$condition,[string]$message) { if(!$condition){throw $message} }
$optimizerAssembly=[Reflection.Assembly]::LoadFrom((Join-Path $repository 'tmp\political-border-optimizer\bin\Win64_Shipping_Client\AgesOfCalradia.PoliticalBorderOptimizer.dll'))
$optimizerType=$optimizerAssembly.GetType('AgesOfCalradia.PoliticalBorderOptimizer.PoliticalBorderOptimizerSubModule',$true)
$optimizerRuntime=$optimizerAssembly.GetType('AgesOfCalradia.PoliticalBorderOptimizer.BorderOptimizerRuntime',$true)
$optimizerHooks=$optimizerAssembly.GetType('AgesOfCalradia.PoliticalBorderOptimizer.BorderOptimizerPatches',$true)
$reviewedOwners=@()
$queueFixture=$null
$queueFixtureDirectory=$null
try {
 foreach($entry in @(
  @('PoliticalFillSeamFix','NativeFillSeamFix','aoc.political-fill-seam-fix.v1','Advance','AdvanceStart','','AdvanceFinished'),
  @('PoliticalRenderDiagnostics','NativeRenderProbe','aoc.tests.political-render-probe.v1','Advance','AdvanceStart','AdvanceEnd',''),
  @('PoliticalRenderDiagnostics','NativeRenderProbe','aoc.tests.political-render-probe.v1','AddFrontierSegment','SegmentStart','SegmentEnd',''),
  @('PoliticalRenderDiagnostics','NativeRenderProbe','aoc.tests.political-render-probe.v1','AddDoubleSidedQuad','QuadStart','',''),
  @('CoastSurfaceFix','CoastSurfacePatch','aoc.coast-surface-fix.v1','Advance','AdvanceStart','','AdvanceEnd'),
  @('CoastSurfaceFix','CoastSurfacePatch','aoc.coast-surface-fix.v1','AddFrontierSegment','SegmentStart','','SegmentEnd'),
  @('CoastSurfaceFix','CoastSurfacePatch','aoc.coast-surface-fix.v1','AddDoubleSidedFanTriangle','CapStart','','')
 )) {
  $sidecar=[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir ('Modules\AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.'+$entry[0]+'.dll')))
  $patchType=$sidecar.GetType(('AgesOfCalradia.'+$entry[0]+'.'+$entry[1]),$true)
  $owner=New-Object HarmonyLib.Harmony($entry[2]);$reviewedOwners+=$owner
  $hooks=@($null,$null,$null)
  for($i=0;$i -lt 3;$i++){if($entry[$i+4]){$hooks[$i]=New-Object HarmonyLib.HarmonyMethod($patchType.GetMethod($entry[$i+4],$flags))}}
  [void]$owner.Patch([HarmonyLib.AccessTools]::Method($builder,$entry[3]),$hooks[0],$hooks[1],$null,$hooks[2])
 }
 $bindings.GetField('Ready',$flags).SetValue($null,$false)
 $publishedType=$assembly.GetType('Aoc.BorderEditPrototype.PublishedBorderSubModule',$true)
 $published=[Activator]::CreateInstance($publishedType)
 $publishedType.GetMethod('OnSubModuleLoad',$callbacks).Invoke($published,@())
 Assert-Composition ($bindings.GetField('Ready',$flags).GetValue($null)) 'Published runtime did not bind.'
 $optimizer=[Activator]::CreateInstance($optimizerType)
 $optimizerType.GetMethod('OnSubModuleLoad',$callbacks).Invoke($optimizer,@())
 $advance=[HarmonyLib.AccessTools]::Method($builder,'Advance')
 $owners=[HarmonyLib.Harmony]::GetPatchInfo($advance).Owners
 foreach($id in @('aoc.political-fill-seam-fix.v1','aoc.coast-surface-fix.v1','aoc.tests.political-render-probe.v1','Aoc.BorderEditPrototype.v01','AgesOfCalradia.PoliticalBorderOptimizer')) {
  Assert-Composition ($owners -contains $id) "Combined Advance patches missing $id"
 }
 $grid=$core.GetType('TwelveMonthCalendar.CampaignMapTerrainGridCache',$true)
 foreach($name in @('TrySampleExactHeight','TryGetNativeTerrain')) {
  $info=[HarmonyLib.Harmony]::GetPatchInfo([HarmonyLib.AccessTools]::Method($grid,$name))
  Assert-Composition (@($info.Prefixes|Where-Object owner -eq 'AgesOfCalradia.PoliticalBorderOptimizer').Count -eq 1) "Missing actual optimizer prefix: $name"
  Assert-Composition (@($info.Postfixes|Where-Object owner -eq 'AgesOfCalradia.PoliticalBorderOptimizer').Count -eq 1) "Missing actual optimizer postfix: $name"
 }
 Write-Output 'PASS: Published then optimizer actual startup hooks coexist with pinned seam, coast, and diagnostic patches.'
 $warmup=$optimizerAssembly.GetType('AgesOfCalradia.PoliticalBorderOptimizer.EagerPoliticalBorderWarmup',$true)
 $mapScreenType=[HarmonyLib.AccessTools]::TypeByName('SandBox.View.Map.MapScreen')
 $dismissInfo=[HarmonyLib.Harmony]::GetPatchInfo([HarmonyLib.AccessTools]::Method($mapScreenType,'HandleIfBlockerStatesDisabled'))
 Assert-Composition (@($dismissInfo.Transpilers|Where-Object owner -eq 'AgesOfCalradia.PoliticalBorderOptimizer').Count -eq 1) 'Actual native loading-dismissal transpiler was not installed.'
 $closeInfo=[HarmonyLib.Harmony]::GetPatchInfo([HarmonyLib.AccessTools]::Method($mapScreenType,'OnFinalize'))
 Assert-Composition (@($closeInfo.Prefixes|Where-Object owner -eq 'AgesOfCalradia.PoliticalBorderOptimizer').Count -eq 1) 'Actual map-close cleanup was not installed.'
 $behaviorType=$core.GetType('TwelveMonthCalendar.CampaignKingdomBorderBehavior',$true)
 foreach($targetName in @('OnSessionLaunched','OnGameLoadFinished')) {
  $hook=[HarmonyLib.Harmony]::GetPatchInfo([HarmonyLib.AccessTools]::Method($behaviorType,$targetName))
  Assert-Composition (@($hook.Postfixes|Where-Object owner -eq 'AgesOfCalradia.PoliticalBorderOptimizer').Count -eq 1) "Missing published startup queue hook: $targetName"
 }
 $fixtureRoot=Join-Path $repository 'tmp\political-border-optimizer\AuthoredBorders'
 if(!(Test-Path -LiteralPath $fixtureRoot)) {
  $queueFixtureDirectory=$fixtureRoot
  New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
  $queueFixture=Join-Path $fixtureRoot 'queue-test.xml'
  [IO.File]::WriteAllText($queueFixture,'<fixture/>')
 }
 $pending=$warmup.GetField('_pendingPublishedBehavior',$flags)
 $attempted=$warmup.GetField('_mapPublicationAttempted',$flags)
 $instance=New-Object object
 $warmup.GetMethod('AfterSessionLaunched',$flags).Invoke($null,@($instance))
 Assert-Composition ([object]::ReferenceEquals($instance,$pending.GetValue($null)) -and -not $attempted.GetValue($null)) 'Session launch did not queue an initial readiness attempt.'
 $attempted.SetValue($null,$true)
 $warmup.GetMethod('AfterGameLoadFinished',$flags).Invoke($null,@($instance))
 Assert-Composition ($attempted.GetValue($null)) 'Duplicate startup event incorrectly restarted a bounded attempt.'
 $nextInstance=New-Object object
 $warmup.GetMethod('AfterSessionLaunched',$flags).Invoke($null,@($nextInstance))
 Assert-Composition ([object]::ReferenceEquals($nextInstance,$pending.GetValue($null)) -and -not $attempted.GetValue($null)) 'A new campaign did not get a fresh readiness attempt.'
 $gate=$warmup.GetField('LoadingGate',$flags).GetValue($null)
 $screen=New-Object object
 $gate.GetType().GetMethod('Bind',$callbacks).Invoke($gate,@($screen,$nextInstance))
 $warmup.GetMethod('MapLoadingClosed',$flags).Invoke($null,@($screen))
 Assert-Composition ($pending.GetValue($null) -eq $null -and $gate.GetType().GetProperty('State',$callbacks).GetValue($gate,$null).ToString() -eq 'Idle') 'Closing the owning map did not clear its loading hold.'
 Write-Output 'PASS: actual session/load hooks queue first publication, preserve single-attempt behavior, and reset for a new campaign.'
 # Enable a complete synthetic prepared grid, proving that a published miss
 # bypasses an available approximation rather than merely an absent grid.
 function Set-ProbeField($name,$value) { $optimizerRuntime.GetField($name,$flags).SetValue($null,$value) }
 Set-ProbeField '_publishedLayoutActive' $true
 Set-ProbeField '_topologyReady' $true
 Set-ProbeField '_buildingTopology' $false
 Set-ProbeField '_preparedRows' 1
 foreach($name in @('_minimumX','_minimumY')) { Set-ProbeField $name ([single]0) }
 Set-ProbeField '_maximumX' ([single]384);Set-ProbeField '_maximumY' ([single]1)
 Set-ProbeField '_preparedStepX' ([single]1);Set-ProbeField '_preparedStepY' ([single]1)
 Set-ProbeField '_preparedHeights' ([single[]]@(1..770|ForEach-Object {999}))
 Set-ProbeField '_preparedHeightsValid' ([bool[]]@(1..770|ForEach-Object {$true}))
 Set-ProbeField '_preparedTerrain' ([TaleWorlds.Core.TerrainType[]]@(1..384|ForEach-Object {[TaleWorlds.Core.TerrainType]::Water}))
 Set-ProbeField '_preparedTerrainValid' ([bool[]]@(1..384|ForEach-Object {$true}))
 $point=New-Object TaleWorlds.Library.Vec2([single]0.25,[single]0.25)
 $nearby=New-Object TaleWorlds.Library.Vec2([single]0.25001,[single]0.25)
 $heightArgs=@($point,[single]0)
 Assert-Composition ($optimizerRuntime.GetMethod('TrySamplePreparedHeight',$flags).Invoke($null,$heightArgs)) 'Synthetic prepared height unavailable.'
 Assert-Composition ($heightArgs[1] -eq [single]999) 'Synthetic prepared height is wrong.'
 $terrainArgs=@($point,[TaleWorlds.Core.TerrainType]::Water,$false)
 Assert-Composition ($optimizerRuntime.GetMethod('TryResolvePreparedTerrain',$flags).Invoke($null,$terrainArgs) -and $terrainArgs[2]) 'Synthetic prepared terrain unavailable.'
 foreach($probe in @(@('ExactHeight',[single]37.125,[single]-123.5),@('NativeTerrain',[TaleWorlds.Core.TerrainType]::Mountain,[TaleWorlds.Core.TerrainType]::Forest))) {
  $prefix=$optimizerHooks.GetMethod('Before'+$probe[0],$flags)
  $postfix=$optimizerHooks.GetMethod('After'+$probe[0],$flags)
  $arguments=@($point,$probe[1],$false)
  Assert-Composition ($prefix.Invoke($null,$arguments)) "$($probe[0]) miss incorrectly consumed prepared grid."
  $null=$postfix.Invoke($null,@($point,$probe[1],$true))
  $arguments=@($point,$probe[2],$false)
  Assert-Composition (-not $prefix.Invoke($null,$arguments)) "$($probe[0]) exact hit fell through."
  Assert-Composition ($arguments[1] -eq $probe[1] -and $arguments[2]) "$($probe[0]) did not replay exact success output."
  $arguments=@($nearby,$probe[1],$false)
  Assert-Composition ($prefix.Invoke($null,$arguments)) "$($probe[0]) nearby coordinates reused another point."
  $null=$postfix.Invoke($null,@($nearby,$probe[2],$false))
  $arguments=@($nearby,$probe[1],$true)
  Assert-Composition (-not $prefix.Invoke($null,$arguments)) "$($probe[0]) exact failure was not memoized."
  Assert-Composition ($arguments[1] -eq $probe[2] -and -not $arguments[2]) "$($probe[0]) failure output was not preserved."
 }
 Write-Output 'PASS: actual published probe hooks fall through on misses despite ready approximation; exact successes and failure outputs replay; nearby coordinates miss.'
} finally {
 if($queueFixture -and (Test-Path -LiteralPath $queueFixture)){Remove-Item -LiteralPath $queueFixture}
 if($queueFixtureDirectory -and (Test-Path -LiteralPath $queueFixtureDirectory)){Remove-Item -LiteralPath $queueFixtureDirectory}
 if($null -ne $optimizer){$optimizerType.GetMethod('OnSubModuleUnloaded',$callbacks).Invoke($optimizer,@())}
 $harmony=$bindings.GetField('Harmony',$flags).GetValue($null)
 if($null -ne $harmony){$harmony.UnpatchAll('Aoc.BorderEditPrototype.v01')}
 foreach($owner in $reviewedOwners){$owner.UnpatchAll($owner.Id)}
}
