param([string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
$gameBin=Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
$root=Split-Path -Parent $PSScriptRoot
$repository=Split-Path -Parent (Split-Path -Parent $root)
[void][Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\0Harmony.dll'))
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.Engine','TaleWorlds.TwoDimension','TaleWorlds.GauntletUI','TaleWorlds.GauntletUI.Data',
 'TaleWorlds.Engine.GauntletUI','TaleWorlds.Core','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.CampaignSystem',
 'TaleWorlds.CampaignSystem.ViewModelCollection','TaleWorlds.InputSystem','TaleWorlds.MountAndBlade','TaleWorlds.MountAndBlade.GauntletUI.Widgets','TaleWorlds.ScreenSystem')) {
 [void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))
}
foreach($pair in @(@('SandBox','SandBox.ViewModelCollection'),@('Native','TaleWorlds.MountAndBlade.View'),@('SandBox','SandBox.View'))) {
 [void][Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir ('Modules\'+$pair[0]+'\bin\Win64_Shipping_Client\'+$pair[1]+'.dll')))
}
$core=[Reflection.Assembly]::LoadFrom((Join-Path $repository 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'))
foreach($name in @('NavalDLC','NavalDLC.ViewModelCollection','NavalDLC.GauntletUI.Widgets','NavalDLC.GauntletUI','NavalDLC.View')) {
 [void][Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir ('Modules\NavalDLC\bin\Win64_Shipping_Client\'+$name+'.dll')))
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'package\AgesOfCalradiaBorderEditPrototype\bin\Win64_Shipping_Client\AgesOfCalradiaBorderEditPrototype.dll'))
$panelType=$assembly.GetType('Aoc.BorderEditPrototype.BorderPanelVM',$true)
$panelVm=[Activator]::CreateInstance($panelType)
[xml]$panelXml=Get-Content (Join-Path $root 'GUI\Prefabs\AocBorderEditPrototype.xml') -Raw
foreach($action in @('Undo','Redo','Save','Delete')) {
 $button=$panelXml.SelectSingleNode(('//ButtonWidget[@Command.Click="Execute'+$action+'"]'))
 if($button.GetAttribute('IsEnabled') -ne ('@Can'+$action) -or $button.GetAttribute('AlphaFactor') -ne ('@'+$action+'Opacity')) { throw "Incorrect enabled/opacity binding for $action" }
 if($panelType.GetProperty('Can'+$action).GetValue($panelVm,$null) -ne $false){throw "Uninitialized $action action is enabled"}
 $opacity=[single]$panelType.GetProperty($action+'Opacity').GetValue($panelVm,$null)
 if([Math]::Abs($opacity-0.4) -gt 0.001){throw "Disabled $action lacks dimmed appearance"}
}
$panelVm.OnFinalize()
Write-Output 'PASS: actual panel view model starts with unavailable actions disabled and dimmed; prefab binds those properties.'
$bindings=$assembly.GetType('Aoc.BorderEditPrototype.NativeBindings',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic,Public'
$reviewedOwners=@()
try {
 # Reproduce installed patch composition, without invoking native scene operations.
 $builder=$core.GetType('TwelveMonthCalendar.CampaignPoliticalTerritoryFill+Builder',$true)
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
  $owner=New-Object HarmonyLib.Harmony($entry[2]); $reviewedOwners+=$owner
  $hooks=@($null,$null,$null)
  for($i=0;$i -lt 3;$i++){ if($entry[$i+4]){$hooks[$i]=New-Object HarmonyLib.HarmonyMethod($patchType.GetMethod($entry[$i+4],$flags))} }
  [void]$owner.Patch([HarmonyLib.AccessTools]::Method($builder,$entry[3]),$hooks[0],$hooks[1],$null,$hooks[2])
 }
 $submoduleType=$assembly.GetType('Aoc.BorderEditPrototype.BorderEditSubModule',$true)
 $submodule=[Activator]::CreateInstance($submoduleType)
 $callbacks=[Reflection.BindingFlags]'Instance,NonPublic'
 $submoduleType.GetMethod('OnSubModuleLoad',$callbacks).Invoke($submodule,@())
 $bound=$bindings.GetField('Ready',$flags).GetValue($null)
 if(-not $bound){throw 'Native binding was deferred rather than verified.'}
 $submoduleType.GetMethod('OnApplicationTick',$callbacks).Invoke($submodule,@([single]0.016))
 if(-not $bindings.GetField('Ready',$flags).GetValue($null)){throw 'Application tick lost startup binding.'}
 $map=$bindings.GetField('MapType',$flags).GetValue($null)
 foreach($name in @('MapCameraView','SceneLayer','IsEscapeMenuOpened','IsInMenu')) {
  if($null -eq [HarmonyLib.AccessTools]::Property($map,$name)){throw "Missing map property $name"}
 }
 $cameraView=[HarmonyLib.AccessTools]::Property($map,'MapCameraView').PropertyType
 if($null -eq [HarmonyLib.AccessTools]::Property($cameraView,'Camera')){throw 'Map camera getter is missing.'}
 # Exercise the actual installed InputInformation value type. Only left drag
 # flags change while editing; normal camera controls and inactive input survive.
 $cameraTick=[HarmonyLib.AccessTools]::Method($cameraView,'OnBeforeTick')
 $inputType=$cameraTick.GetParameters()[0].ParameterType.GetElementType()
 $input=[Activator]::CreateInstance($inputType)
 $leftFields=@('LeftMouseButtonPressed','LeftMouseButtonDown','LeftMouseButtonReleased','LeftButtonDraggingMode')
 $preservedFields=@('RightMouseButtonDown','MiddleMouseButtonDown','CameraFollowModeKeyPressed','RotateLeftKeyDown')
 foreach($name in ($leftFields+$preservedFields)){$inputType.GetField($name).SetValue($input,$true)}
 $filter=$bindings.GetMethod('FilterCameraInput',$flags)
 $filter.Invoke($null,@($input,$false))
 foreach($name in ($leftFields+$preservedFields)){if(-not $inputType.GetField($name).GetValue($input)){throw "Inactive camera field changed: $name"}}
 $filter.Invoke($null,@($input,$true))
 foreach($name in $leftFields){if($inputType.GetField($name).GetValue($input)){throw "Editor camera drag field remains enabled: $name"}}
 foreach($name in $preservedFields){if(-not $inputType.GetField($name).GetValue($input)){throw "Unrelated camera input changed: $name"}}
 # Prove Harmony copies __args back into the by-ref struct before consumption,
 # using a managed consumer so no native camera/scene function is invoked.
 $dynamicName=New-Object Reflection.AssemblyName('BorderCameraInputVerifier')
 $dynamicAssembly=[AppDomain]::CurrentDomain.DefineDynamicAssembly($dynamicName,[Reflection.Emit.AssemblyBuilderAccess]::Run)
 $dynamicModule=$dynamicAssembly.DefineDynamicModule('Verifier')
 $dynamicType=$dynamicModule.DefineType('CameraInputConsumer',[Reflection.TypeAttributes]'Public,Abstract,Sealed')
 $consumer=$dynamicType.DefineMethod('ReadLeftDown',[Reflection.MethodAttributes]'Public,Static',[bool],[Type[]]@($inputType.MakeByRefType()))
 $consumer.SetImplementationFlags([Reflection.MethodImplAttributes]::NoInlining)
 $il=$consumer.GetILGenerator()
 $il.Emit([Reflection.Emit.OpCodes]::Ldarg_0)
 $il.Emit([Reflection.Emit.OpCodes]::Ldfld,$inputType.GetField('LeftMouseButtonDown'))
 $il.Emit([Reflection.Emit.OpCodes]::Ret)
 $consumerMethod=$dynamicType.CreateType().GetMethod('ReadLeftDown')
 $runtimeType=$assembly.GetType('Aoc.BorderEditPrototype.PrototypeRuntime',$true)
 $nativeMouse=[HarmonyLib.AccessTools]::Method($map,'HandleMouse')
 $editorRay=$runtimeType.GetMethod('TryPointerRay',$flags)
 $nativeTranslate=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($nativeMouse) | Where-Object { $_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'TranslateMouse' })
 $editorTranslate=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($editorRay) | Where-Object { $_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'TranslateMouse' })
 if($nativeTranslate.Count -ne 1 -or $editorTranslate.Count -ne 1 -or $nativeTranslate[0].operand -ne $editorTranslate[0].operand){throw 'Editor cursor conversion differs from native scene-view picking'}
 if($runtimeType.GetProperty('SaveState',$flags).GetValue($null,$null) -ne 'No edits'){throw 'Untouched editor falsely claims edits are saved'}
 Write-Output 'PASS: editor uses the same SceneView.TranslateMouse target as native HandleMouse; untouched state says No edits.'
 $instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
 $cellType=$core.GetType('TwelveMonthCalendar.PoliticalTerritoryCell',$true)
 $cellCtor=$cellType.GetConstructors($instanceFlags)[0]
 [TaleWorlds.Library.Vec2]$sitePosition=New-Object TaleWorlds.Library.Vec2([single]2,[single]2)
 $cell=$cellCtor.Invoke(@($sitePosition,'test-owner',[uint32]4281558681))
 $listType=[System.Collections.Generic.List``1].MakeGenericType($cellType)
 $cells=[Activator]::CreateInstance($listType);$cells.Add($cell)
 $indexType=$core.GetType('TwelveMonthCalendar.CampaignPoliticalTerritoryFill+NearestSiteIndex',$true)
 $index=$indexType.GetConstructors($instanceFlags)[0].Invoke([object[]](,$cells))
 $builderInstance=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($builder)
 [HarmonyLib.AccessTools]::Field($builder,'_siteIndex').SetValue($builderInstance,$index)
 $captureType=$assembly.GetType('Aoc.BorderEditPrototype.NativeCapture',$true)
 $capture=[Activator]::CreateInstance($captureType,$true);$captureType.GetField('Builder',$instanceFlags).SetValue($capture,$builderInstance)
 $styleType=$assembly.GetType('Aoc.BorderEditPrototype.NativeLocationStyle',$true)
 $style=$styleType.GetConstructors($instanceFlags)[0].Invoke(@($capture))
 $pointType=$assembly.GetType('Aoc.BorderEditPrototype.Point2',$true)
 $point=$pointType.GetConstructors($instanceFlags)[0].Invoke(@([single]2,[single]2))
 $colour=$styleType.GetMethod('Color',$instanceFlags).Invoke($style,@($point,50))
 if([uint32]$colour -ne [uint32]4279841612){throw "Local fill colour does not match native 50-percent scaling: $colour"}
 Write-Output 'PASS: actual captured native site index supplies location colour with matching fill brightness; land lookup resolves.'
 $activeSetter=$runtimeType.GetProperty('Active',$flags).GetSetMethod($true)
 $cameraHook=$assembly.GetType('Aoc.BorderEditPrototype.NativeHooks',$true).GetMethod('CameraInput',$flags)
 $cameraTestOwner=New-Object HarmonyLib.Harmony('aoc.border-tests.camera-input')
 try {
  [void]$cameraTestOwner.Patch($consumerMethod,(New-Object HarmonyLib.HarmonyMethod($cameraHook)))
  $inputType.GetField('LeftMouseButtonDown').SetValue($input,$true)
  $activeSetter.Invoke($null,@($false))
  if(-not $consumerMethod.Invoke($null,@($input))){throw 'Inactive camera consumer lost left input.'}
  $activeSetter.Invoke($null,@($true))
  if($consumerMethod.Invoke($null,@($input))){throw 'Harmony did not suppress camera left input before consumption.'}
 } finally {
  $activeSetter.Invoke($null,@($false))
  $cameraTestOwner.UnpatchAll('aoc.border-tests.camera-input')
 }
 Write-Output 'PASS: camera left-drag input suppressed before consumption through actual Harmony by-ref hook; inactive and unrelated controls preserved.'
 $elevationType=$dynamicModule.DefineType('CameraElevationConsumer',[Reflection.TypeAttributes]'Public,Abstract,Sealed')
 $elevationMethod=$elevationType.DefineMethod('ReadElevation',[Reflection.MethodAttributes]'Public,Static',[single],[Type[]]@([single],[single],[single]))
 $elevationMethod.SetImplementationFlags([Reflection.MethodImplAttributes]::NoInlining)
 $il=$elevationMethod.GetILGenerator();$il.Emit([Reflection.Emit.OpCodes]::Ldarg_2);$il.Emit([Reflection.Emit.OpCodes]::Ret)
 $elevationConsumer=$elevationType.CreateType().GetMethod('ReadElevation')
 $topDownSetter=$runtimeType.GetProperty('TopDown',$flags).GetSetMethod($true)
 $topDownHook=$assembly.GetType('Aoc.BorderEditPrototype.NativeHooks',$true).GetMethod('CameraTopDown',$flags)
 $topDownOwner=New-Object HarmonyLib.Harmony('aoc.border-tests.top-down')
 try {
  [void]$topDownOwner.Patch($elevationConsumer,(New-Object HarmonyLib.HarmonyMethod($topDownHook)))
  $arguments=@([single]0,[single]0,[single]0.4)
  $topDownSetter.Invoke($null,@($true));$activeSetter.Invoke($null,@($false))
  if([Math]::Abs($elevationConsumer.Invoke($null,$arguments)-0.4) -gt 0.001){throw 'Inactive top-down hook changes elevation'}
  $activeSetter.Invoke($null,@($true))
  if([Math]::Abs($elevationConsumer.Invoke($null,$arguments)-[Math]::PI/2) -gt 0.001){throw '2D editor does not use vertical camera elevation'}
  $topDownSetter.Invoke($null,@($false))
  if([Math]::Abs($elevationConsumer.Invoke($null,$arguments)-0.4) -gt 0.001){throw '3D toggle does not restore native elevation'}
  $topDownSetter.Invoke($null,@($true));$activeSetter.Invoke($null,@($false))
  if([Math]::Abs($elevationConsumer.Invoke($null,$arguments)-0.4) -gt 0.001){throw 'Closing editor leaves vertical elevation active'}
 } finally {
  $activeSetter.Invoke($null,@($false));$topDownSetter.Invoke($null,@($true));$topDownOwner.UnpatchAll('aoc.border-tests.top-down')
 }
 Write-Output 'PASS: actual Harmony top-down hook sets 90 degrees only in 2D editing; 3D toggle and closing restore native elevation.'
 $behaviorType=$core.GetType('TwelveMonthCalendar.CampaignKingdomBorderBehavior',$true)
 $presentation=[HarmonyLib.AccessTools]::Method($behaviorType,'ApplyFrontierZoomPresentation')
 if($null -eq $presentation -or $presentation.GetParameters().Count -ne 1 -or $presentation.GetParameters()[0].ParameterType -ne [bool]){throw 'Unsupported frontier presentation target'}
 $frontierHook=$assembly.GetType('Aoc.BorderEditPrototype.NativeHooks',$true).GetMethod('FrontierPresentation',$flags)
 $behaviorField=$runtimeType.GetField('_behavior',$flags)
 $instance=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($behaviorType)
 try {
  $behaviorField.SetValue($null,$instance)
  if(-not $frontierHook.Invoke($null,@($instance))){throw 'Inactive editor blocks normal presentation'}
  $activeSetter.Invoke($null,@($true))
  if($frontierHook.Invoke($null,@($instance))){throw 'Active editor allows normal borders to reappear'}
  if(-not $frontierHook.Invoke($null,@((New-Object object)))){throw 'Editor suppresses another behavior instance'}
  # A skipped real target touches no uninitialized entity lists or native scene calls.
  $presentation.Invoke($instance,@($true))
 } finally {
  $activeSetter.Invoke($null,@($false))
  $behaviorField.SetValue($null,$null)
 }
 Write-Output 'PASS: real frontier presentation hook prevents native reveal while editing; inactive and unrelated instances pass through.'
 $camera=$assembly.GetType('Aoc.BorderEditPrototype.PrototypeRuntime',$true)
 $click=$assembly.GetType('Aoc.BorderEditPrototype.NativeHooks',$true).GetMethod('MapClick',$flags)
 if(-not $click.Invoke($null,@())){throw 'Inactive editor blocked normal map clicks.'}
 $unknown=New-Object HarmonyLib.Harmony('aoc.border-tests.unknown-owner')
 try {
  [void]$unknown.Patch([HarmonyLib.AccessTools]::Method($builder,'Advance'),(New-Object HarmonyLib.HarmonyMethod($click)))
  $rejected=$false
  try { $bindings.GetMethod('Patch',$flags).Invoke($null,@($builder,'Advance','AdvanceStart','AdvanceEnd','AdvanceFailure')) }
  catch { $rejected=$_.Exception.GetBaseException().Message -like 'Unsupported patch on*unknown-owner*' }
  if(-not $rejected){throw 'Unknown builder patch was not rejected.'}
 } finally { $unknown.UnpatchAll('aoc.border-tests.unknown-owner') }
 $constants=$core.GetType('TwelveMonthCalendar.CampaignPoliticalTerritoryFill',$true)
 $nativeKey=$core.GetType('TwelveMonthCalendar.CampaignPoliticalTerritoryFill+FrontierPointKey',$true)
 $keyConstructor=$nativeKey.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic,Public')[0]
 $keyX=$nativeKey.GetField('_x',[Reflection.BindingFlags]'Instance,NonPublic')
 $keyY=$nativeKey.GetField('_y',[Reflection.BindingFlags]'Instance,NonPublic')
 $prototypeKey=$assembly.GetType('Aoc.BorderEditPrototype.NativeCapture',$true).GetMethod('PointKey',$flags)
 for($sampleIndex=0;$sampleIndex -lt 4096;$sampleIndex++) {
  $position=New-Object TaleWorlds.Library.Vec2([single](200.00049+$sampleIndex*0.0000152587890625),[single](-120.0005-$sampleIndex*0.0000152587890625))
  $key=$keyConstructor.Invoke(@($position))
  $expectedKey=[string]$keyX.GetValue($key)+':'+[string]$keyY.GetValue($key)
  if($prototypeKey.Invoke($null,@($position)) -ne $expectedKey){throw "Endpoint identity differs from protected renderer at $position"}
 }
 Write-Output 'PASS: 4096 fractional-boundary endpoint identities match the protected native key constructor.'
 foreach($entry in @(@('FrontierWidth',[single]1.6),@('FrontierCapStepsPerHalf',4),@('FrontierMeshRenderOrder',108),@('FrontierHeight',[single]5))) {
  $actual=[HarmonyLib.AccessTools]::Field($constants,$entry[0]).GetRawConstantValue()
  if($actual -ne $entry[1]){throw "Native style constant differs: $($entry[0]) = $actual"}
 }
 Write-Output 'PASS: startup callbacks bind alongside hash-verified installed fill/coast/diagnostics hooks; unknown owner rejected; inactive clicks and native style constants agree.'
} finally {
 $harmony=$bindings.GetField('Harmony',$flags).GetValue($null)
 if($null -ne $harmony){$harmony.UnpatchAll('Aoc.BorderEditPrototype.v01')}
 foreach($owner in $reviewedOwners){$owner.UnpatchAll($owner.Id)}
}
