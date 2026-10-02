param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$life=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyShipLifecycleObserver',$true)
$reward=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyRewardObserver',$true)
$instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
$h=[HarmonyLib.Harmony]::new('aoc.ship-lifecycle.fixture')
try{
 $life.GetMethod('Install',$flags).Invoke($null,@($h))|Out-Null
 $targets=@($life.GetMethod('Targets',$flags).Invoke($null,@()))
 foreach($target in $targets){if(!([HarmonyLib.Harmony]::GetPatchInfo($target).Owners -contains 'aoc.ship-lifecycle.fixture')){throw 'Lifecycle target missing'}}
 $calls=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($targets[0])|Where-Object {$_.operand -is [Reflection.MethodInfo]}|ForEach-Object {$_.operand})
 if(!@($calls|Where-Object {$_.DeclaringType -eq [TaleWorlds.CampaignSystem.Actions.DestroyShipAction] -and $_.Name -eq 'Apply'}).Count){throw 'Native removal no longer encloses ship destruction'}
 $party=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.MobileParty])
 $partyBase=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Party.PartyBase])
 $ship=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Naval.Ship])
 $party.GetType().GetFields($instanceFlags)|Where-Object FieldType -eq ([TaleWorlds.CampaignSystem.Party.PartyBase])|ForEach-Object {$_.SetValue($party,$partyBase)}
 $field=$partyBase.GetType().GetField('_ships',$instanceFlags)
 $ships=[Activator]::CreateInstance($field.FieldType);$field.SetValue($partyBase,$ships);$ships.Add($ship)
 $path=$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
 $ownerArgs=[object[]]@($ship,$null)
 $life.GetMethod('BeforeOwner',$flags).Invoke($null,$ownerArgs)|Out-Null
 $ship.GetType().GetField('_owner',$instanceFlags).SetValue($ship,$partyBase)
 $life.GetMethod('AfterOwner',$flags).Invoke($null,@($ship,$ownerArgs[1],$true,$null))|Out-Null
 $ship.GetType().GetField('_owner',$instanceFlags).SetValue($ship,$null)
 $before=$life.GetMethod('BeforeRemoval',$flags);$after=$life.GetMethod('AfterRemoval',$flags)
 $argsBefore=[object[]]@($party,$null);$before.Invoke($null,$argsBefore)|Out-Null
 $after.Invoke($null,@($argsBefore[1],$false,$null))|Out-Null
 $argsBefore=[object[]]@($party,$null);$before.Invoke($null,$argsBefore)|Out-Null
 $ships.Clear()
 $after.Invoke($null,@($argsBefore[1],$true,$null))|Out-Null
 $capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
 $owners=@($rows|Where-Object kind -eq SHIP_OWNER_CHANGE)
 if($owners.Count -ne 1 -or $owners[0].detail -notmatch 'from=none; to=other_owner' -or $owners[0].detail -notmatch 'ship=1;'){throw 'Ownership transition missing stable ship identity'}
 $ends=@($rows|Where-Object kind -eq SHIP_LIFECYCLE_END)
 if($ends.Count -ne 2 -or $ends[0].detail -notmatch 'originalRan=False' -or $ends[1].detail -notmatch 'originalRan=True'){throw 'Skipped removal counted as executed'}
 $members=@($rows|Where-Object {$_.kind -eq 'SHIP_MEMBERSHIP' -and $_.metric -eq 'after'})
 if($members[0].after -ne '1' -or $members[1].after -ne '0' -or $members[1].detail -notmatch 'ship=1; owner=none'){throw 'Ship membership identity/clearance lost'}
 if(@($rows|Where-Object kind -eq SHIP_DESTRUCTION).Count){throw 'List removal fabricated destruction'}
 $capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))|Out-Null
 $argsBefore=[object[]]@($party,$null);$before.Invoke($null,$argsBefore)|Out-Null
 $after.Invoke($null,@($argsBefore[1],$true,[Exception]::new('native removal sentinel')))|Out-Null
 if($after.ReturnType -ne [void] -or $capture.GetProperty('Active',$flags).GetValue($null,$null)){throw 'Native exception not preserved/fail-closed'}
}finally{$h.UnpatchAll('aoc.ship-lifecycle.fixture')}
'PASS: installed native removal/ownership targets and cleanup call, skipped vs executed callbacks, stable ship identity, no invented destruction, exception-safe closure. Synthetic callback fixtures, not live acceptance.'
