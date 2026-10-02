param([string]$GameRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Run with Windows PowerShell: Harmony 2.4.2 requires its .NET Framework runtime.'}
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameBin=Join-Path $GameRoot 'bin\Win64_Shipping_Client'
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]$eventArgs.Name).Name+'.dll'
    foreach($folder in @($gameBin,(Join-Path $GameRoot 'Modules\SandBox\bin\Win64_Shipping_Client'),(Join-Path $GameRoot 'Modules\SandBoxCore\bin\Win64_Shipping_Client'))){
        $path=Join-Path $folder $name
        if(Test-Path -LiteralPath $path){return [Reflection.Assembly]::LoadFrom($path)}
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$probeType=$null
try{
    [void][Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'))
    $probe=[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'bin\Release\AgesOfCalradia.PoliticalRenderDiagnostics.dll'))
    $probeType=$probe.GetType('AgesOfCalradia.PoliticalRenderDiagnostics.NativeRenderProbe',$true)
    $flags=[Reflection.BindingFlags]'Static,NonPublic'
    $probeType.GetMethod('Initialize',$flags).Invoke($null,@())
    if(-not $probeType.GetField('_enabled',$flags).GetValue($null)){throw 'Probe initialization failed; inspect generated identity-timeline.log.'}
    $gateType=$probe.GetType('AgesOfCalradia.PoliticalRenderDiagnostics.RenderThreadGate',$true)
    $gate=[Activator]::CreateInstance($gateType,$true)
    $gateFlags=[Reflection.BindingFlags]'Instance,NonPublic'
    $observe=$gateType.GetMethod('Observe',$gateFlags)
    $bind=$gateType.GetMethod('BindApplicationTick',$gateFlags)
    if($observe.Invoke($gate,@(25)).ToString() -ne 'AwaitingApplicationTick'){throw 'Loader thread incorrectly established native affinity.'}
    if($observe.Invoke($gate,@(1)).ToString() -ne 'AwaitingApplicationTick'){throw 'Callback before first app tick must skip, not fail or bind.'}
    if($bind.Invoke($gate,@(1)).ToString() -ne 'Allowed'){throw 'First application tick did not bind thread 1.'}
    if($observe.Invoke($gate,@(1)).ToString() -ne 'Allowed'){throw 'Bound application thread observation rejected.'}
    if($bind.Invoke($gate,@(2)).ToString() -ne 'UnexpectedThread'){throw 'Later application thread mismatch did not fail.'}
    if($observe.Invoke($gate,@(25)).ToString() -ne 'UnexpectedThread'){throw 'Loader thread incorrectly accepted after binding.'}
    if($gateType.GetProperty('BoundThreadId',$gateFlags).GetValue($gate,$null) -ne 1){throw 'Thread mismatch rebound affinity.'}
    # Invoke only diagnostic Tick with no builder/scene; proves binding precedes
    # the no-builder early return without issuing any native scene call.
    $probeType.GetMethod('Tick',$flags).Invoke($null,@())
    $liveGate=$probeType.GetField('ThreadGate',$flags).GetValue($null)
    if($gateType.GetProperty('BoundThreadId',$gateFlags).GetValue($liveGate,$null) -ne [Threading.Thread]::CurrentThread.ManagedThreadId){throw 'Diagnostic Tick failed to bind before the builder-null guard.'}
    Write-Output 'PASS: loader25 cannot bind; callbacks before tick skip; app1 binds; observer1 succeeds; later app2 fails without rebinding; Tick binds before builder-null guard.'
    $targets=@([HarmonyLib.Harmony]::GetAllPatchedMethods() | Where-Object { [HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'aoc.tests.political-render-probe.v1' })
    if($targets.Count -ne 10){throw "Expected 10 diagnostic targets, got $($targets.Count)."}
    foreach($target in $targets){
        $patches=[HarmonyLib.Harmony]::GetPatchInfo($target)
        foreach($patch in @($patches.Prefixes)+@($patches.Postfixes)){
            if($patch.owner -eq 'aoc.tests.political-render-probe.v1' -and $patch.PatchMethod.ReturnType -ne [void]){throw 'Diagnostic callbacks must not replace return values or skip originals.'}
        }
    }
    # Exercise bookkeeping without any native target or native GameEntity call.
    $inference=$probe.GetType('AgesOfCalradia.PoliticalRenderDiagnostics.NativeAlphaAssignmentProbe',$true)
    $snapshotType=$inference.GetNestedType('Snapshot',[Reflection.BindingFlags]'NonPublic')
    $snapshot=[Activator]::CreateInstance($snapshotType,$true)
    $instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic'
    $snapshotType.GetField('EntityCount',$instanceFlags).SetValue($snapshot,1)
    $snapshotType.GetField('Ready',$instanceFlags).SetValue($snapshot,$true)
    $owner=[object]::new()
    $after=$inference.GetMethod('After',$flags)
    $applied=$inference.GetField('_inferredApplied',$flags)
    $snapshotType.GetField('Alpha',$instanceFlags).SetValue($snapshot,[single]0.6)
    $after.Invoke($null,@($owner,$true,$true,$snapshot))
    if([Math]::Abs($applied.GetValue($null)-0.6) -gt 0.0001){throw 'An eligible original assignment was not tracked.'}
    $snapshotType.GetField('Alpha',$instanceFlags).SetValue($snapshot,[single]1)
    $after.Invoke($null,@($owner,$false,$true,$snapshot))
    if([Math]::Abs($applied.GetValue($null)-0.6) -gt 0.0001){throw 'A skipped original branch incorrectly changed inferred alpha at full request.'}
    $after.Invoke($null,@($owner,$true,$false,$snapshot))
    if([Math]::Abs($applied.GetValue($null)-0.6) -gt 0.0001){throw 'A suppressed original incorrectly changed inferred alpha.'}
    $after.Invoke($null,@($owner,$true,$true,$snapshot))
    if($applied.GetValue($null) -ne 1){throw 'An eligible final full assignment was not tracked.'}
    Write-Output 'PASS: synthetic alpha inference tracks assignments, preserves stale full-request alpha, and honors runOriginal=false.'
    $coastType=$probeType.Assembly.GetType('AgesOfCalradia.PoliticalRenderDiagnostics.CoastlineEvidenceProbe',$true)
    $interpret=$coastType.GetMethod('Interpret',$flags)
    foreach($pair in @(@($true,$false),@($false,$true))){
        if($interpret.Invoke($null,$pair) -ne 'land-nonland-boundary-verify-water-or-exclusion'){throw 'A land/nonland transition was prematurely labeled as sea.'}
    }
    if($interpret.Invoke($null,@($true,$true)) -ne 'inland-keep-unchanged'){throw 'Inland classification guard failed.'}
    if($interpret.Invoke($null,@($false,$false)) -ne 'no-political-land-support'){throw 'Unsupported candidate classification failed.'}
    $approvedCore=[AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {$_.GetName().Name -eq 'AgesOfCalradia'}
    $cell=$approvedCore.GetType('TwelveMonthCalendar.PoliticalTerritoryCell',$true)
    foreach($property in @('OwnerKey','Color')){if(-not [HarmonyLib.AccessTools]::Property($cell,$property)){throw "Missing captured identity property $property"}}
    $terrain=$approvedCore.GetType('TwelveMonthCalendar.CampaignMapTerrainGridCache',$true)
    $terrainProbe=[HarmonyLib.AccessTools]::Method($terrain,'TryGetNativeTerrain')
    if(-not $terrainProbe -or -not $terrainProbe.GetParameters()[1].IsOut){throw 'Native terrain probe signature changed.'}
    $fillType=$approvedCore.GetType('TwelveMonthCalendar.CampaignPoliticalTerritoryFill',$true)
    $regionType=$fillType.GetNestedType('FrontierRegion',[Reflection.BindingFlags]'NonPublic')
    $regionConstructor=$regionType.GetConstructors([Reflection.BindingFlags]'Instance,Public,NonPublic') | Where-Object {$_.GetParameters().Count -eq 2}
    $regionRead=$coastType.GetMethod('RegionValue',$flags)
    foreach($isLand in @($true,$false)){
        $region=$regionConstructor.Invoke(@($null,$isLand))
        if($regionRead.Invoke($null,@($region,'Land')) -ne $isLand){throw 'Actual FrontierRegion Land property was not read correctly.'}
        if($null -ne $regionRead.Invoke($null,@($region,'Owner'))){throw 'Actual FrontierRegion null Owner property was not read correctly.'}
    }
    Write-Output 'PASS: real approved FrontierRegion constructor and Land/Owner property reads for land and nonland, without native calls.'
    Write-Output 'PASS: four coastline classification guards; pinned owner identity properties and native terrain out-parameter verified without native calls.'
    Write-Output 'PASS: all 10 exact approved targets patched successfully; Harmony parameter binding validated; callbacks return void. No native target invoked.'
}finally{
    if($probeType){$probeType.GetMethod('Stop',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
