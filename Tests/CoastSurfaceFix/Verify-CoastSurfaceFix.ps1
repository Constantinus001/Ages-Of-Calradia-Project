param([string]$GameRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Run in Windows PowerShell for Harmony 2.4.2.'}
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
$type=$null
$fillType=$null
$probeType=$null
try {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'))
    $flags=[Reflection.BindingFlags]'Static,NonPublic'
    $fillPath=Join-Path $root 'Builds\PoliticalFillSeamFix\bin\Release\AgesOfCalradia.PoliticalFillSeamFix.dll'
    if((Get-FileHash -LiteralPath $fillPath).Hash -ne '0918C5DBB2AD59BFAB9F768EE781D9F86A6DDDDE342D6D76A2BFA52EB88F063E'){throw 'Coexistence test requires exact accepted fill.'}
    $fillAssembly=[Reflection.Assembly]::LoadFrom($fillPath)
    $fillType=$fillAssembly.GetType('AgesOfCalradia.PoliticalFillSeamFix.NativeFillSeamFix',$true)
    $fillType.GetMethod('Initialize',$flags).Invoke($null,@())
    if(-not $fillType.GetField('_enabled',$flags).GetValue($null)){throw 'Accepted fill failed to bind.'}
    $probeAssembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'Tests\PoliticalRenderDiagnostics\bin\Release\AgesOfCalradia.PoliticalRenderDiagnostics.dll'))
    $probeType=$probeAssembly.GetType('AgesOfCalradia.PoliticalRenderDiagnostics.NativeRenderProbe',$true)
    $probeType.GetMethod('Initialize',$flags).Invoke($null,@())
    if(-not $probeType.GetField('_enabled',$flags).GetValue($null)){throw 'Read-only diagnostics failed to bind.'}
    $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'Builds\CoastSurfaceFix\bin\Release\AgesOfCalradia.CoastSurfaceFix.dll'))
    $type=$assembly.GetType('AgesOfCalradia.CoastSurfaceFix.CoastSurfacePatch',$true)
    $flags=[Reflection.BindingFlags]'Static,NonPublic'
    $type.GetMethod('Initialize',$flags).Invoke($null,@())
    if(-not $type.GetField('_enabled',$flags).GetValue($null)){throw 'Coast correction initialization failed.'}
    $targets=@([HarmonyLib.Harmony]::GetAllPatchedMethods() | Where-Object {[HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'aoc.coast-surface-fix.v1'})
    if($targets.Count -ne 6 -or @($targets | Where-Object {$_.Name -notin @('Advance','AddFrontierSegment','TryGetFrontierPoint','BuildNextFrontierRow','HasFrontierLandSupport','AddDoubleSidedFanTriangle')}).Count){throw 'Expected exactly six coast-context, planning, point and cap targets.'}
    if($type.GetField('_thread',$flags).GetValue($null) -ne 0){throw 'Loader must not bind the native thread.'}
    $type.GetMethod('BindThread',$flags).Invoke($null,@())
    if($type.GetField('_thread',$flags).GetValue($null) -ne [Threading.Thread]::CurrentThread.ManagedThreadId){throw 'Application thread binding failed.'}
    $cacheType=$type.GetNestedType('Cache',[Reflection.BindingFlags]'NonPublic')
    [void][Activator]::CreateInstance($cacheType)
    Write-Output 'PASS: six exact pinned targets bind alongside accepted fill and read-only diagnostics; cache constructs; application tick owns the scene thread.'

    $policy=$assembly.GetType('AgesOfCalradia.CoastSurfaceFix.CoastSurfacePolicy',$true)
    $isCoast=$policy.GetMethod('IsCoast',$flags)
    foreach($l in @($false,$true)){foreach($r in @($false,$true)){foreach($terrain in @('CoastalSea','Mountain','unknown')){
        $expected=($l -ne $r -and $terrain -eq 'CoastalSea')
        if($isCoast.Invoke($null,@($l,$r,$terrain)) -ne $expected){throw 'Island/inland/unknown classification regression.'}
    }}}
    $project=$policy.GetMethod('Project',$flags)
    foreach($bad in @([single]::NaN,[single]::PositiveInfinity,[single]::NegativeInfinity)){
        foreach($args in @(@($true,$bad,$false,[single]0,[single]0),@($true,[single]2,$true,$bad,[single]0))){
            if($project.Invoke($null,$args)){throw 'Invalid required height accepted.'}
        }
    }
    $args=@($false,[single]0,$false,[single]0,[single]0)
    if($project.Invoke($null,$args)){throw 'Failed query accepted as zero.'}
    foreach($case in @(@(2,4,9),@(6,4,11))){
        $args=@($true,[single]$case[0],$true,[single]$case[1],[single]0)
        if(-not $project.Invoke($null,$args) -or $args[4] -ne $case[2]){throw 'Water floor must never lower land or inherit seabed height.'}
        for($i=0;$i -le 10;$i++){
            $visible=$args[4]-4.65*(1-$i/10.0)
            if($visible-[Math]::Max($case[0],$case[1]) -lt .3499){throw 'Zoom can submerge a corrected sample.'}
        }
    }
    Write-Output 'PASS: coast-only selection, unknown/excluded/inland guards, failed/nonfinite samples, water floor and zoom endpoints/intermediate values.'

    $point=[TaleWorlds.Library.Vec2]::new(4,6)
    $points=[System.Collections.Generic.Dictionary[TaleWorlds.Library.Vec2,single]]::new()
    $points.Add($point,[single]12)
    $field=$type.GetField('_points',$flags)
    $field.SetValue($null,$points)
    $callback=$type.GetMethod('PointEnd',$flags)
    $vx=[TaleWorlds.Library.Vec3].GetField('x');$vy=[TaleWorlds.Library.Vec3].GetField('y');$vz=[TaleWorlds.Library.Vec3].GetField('z')
    foreach($success in @($false,$true)){
        $args=@($point,[TaleWorlds.Library.Vec3]::new(4,6,3),$success)
        $callback.Invoke($null,$args)
        $expected=if($success){12}else{3}
        if($vz.GetValue($args[1]) -ne $expected -or $vx.GetValue($args[1]) -ne 4 -or $vy.GetValue($args[1]) -ne 6){throw 'Point patch changed XY or revived a rejected original sample.'}
    }
    $field.SetValue($null,$null)
    $args=@($point,[TaleWorlds.Library.Vec3]::new(4,6,3),$true)
    $callback.Invoke($null,$args)
    if($vz.GetValue($args[1]) -ne 3){throw 'Out-of-coast point changed.'}
    $restore=$type.GetField('_restoreSupport',$flags)
    $supportCallback=$type.GetMethod('SupportEnd',$flags)
    $restore.SetValue($null,$true)
    $args=@($false);$supportCallback.Invoke($null,$args)
    if($args[0]){throw 'Support revived without prepared heights.'}
    $field.SetValue($null,$points)
    $args=@($false);$supportCallback.Invoke($null,$args)
    if(-not $args[0]){throw 'Preflighted planned repair did not enable its span.'}
    $restore.SetValue($null,$false)
    $args=@($false);$supportCallback.Invoke($null,$args)
    if($args[0]){throw 'Unplanned gap revived.'}
    $field.SetValue($null,$null)
    Write-Output 'PASS: actual point postfix changes only eligible Z, preserves original rejection and leaves out-of-scope points unchanged.'

    $capture=Join-Path $root 'output/diagnostics/stripes-20260908/capture-20260909-162030-656'
    $coasts=[Collections.Generic.HashSet[string]]::new()
    foreach($row in Import-Csv (Join-Path $capture 'coastline-paths.csv')){
        if($row.accepted -ne 'True'){continue}
        $nonland=if($row.leftLand -eq 'False'){$row.leftNativeTerrain}else{$row.rightNativeTerrain}
        if($isCoast.Invoke($null,@(($row.leftLand -eq 'True'),($row.rightLand -eq 'True'),$nonland))){[void]$coasts.Add($row.segment)}
    }
    $samples=0;$previouslyBuried=0
    foreach($row in Import-Csv (Join-Path $capture 'coastline-surfaces.csv')){
        if(-not $coasts.Contains($row.segment) -or $row.surfaceQueryValid -ne 'True'){continue}
        $height=[single]::Parse($row.campaignSurface,[Globalization.CultureInfo]::InvariantCulture)
        $args=@($true,$height,$false,[single]0,[single]0)
        if(-not $project.Invoke($null,$args)){throw 'Valid captured sample rejected.'}
        if([Math]::Abs(($args[4]-4.65-$height)-.35) -gt .0001){throw 'Captured point still below its measured campaign surface.'}
        if([single]::Parse($row.closeSurfaceClearance,[Globalization.CultureInfo]::InvariantCulture) -lt 0){$previouslyBuried++}
        $samples++
    }
    if($samples -ne 33960 -or $previouslyBuried -ne 19167){throw "Capture coverage changed: samples=$samples previouslyBuried=$previouslyBuried"}
    Write-Output "PASS: $samples confirmed-coast captured samples project above their measured surface, including $previouslyBuried previously below it. Water rendering and triangle interiors still require runtime evidence."
} finally {
    if($type){$type.GetMethod('Stop',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())}
    foreach($owner in @('aoc.political-fill-seam-fix.v1','aoc.tests.political-render-probe.v1')){([HarmonyLib.Harmony]::new($owner)).UnpatchAll($owner)}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
