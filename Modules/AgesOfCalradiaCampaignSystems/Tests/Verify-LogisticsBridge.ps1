param([string]$BaselineRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
$ErrorActionPreference='Stop'
$frameworkAuditRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $frameworkAuditRoot 'Builds\Approved560CalendarFixes\Verify-NativeWorkshopBatch.ps1') -ModuleRoot $BaselineRoot
$logisticsCandidate=Join-Path $frameworkAuditRoot 'output\campaign-systems-logistics\AgesOfCalradiaLogistics.dll'
$logisticsAssembly=[Reflection.Assembly]::LoadFrom($logisticsCandidate)
$reserveType=$logisticsAssembly.GetType('AgesOfCalradiaLogistics.LogisticsReserveBehavior',$true)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$instance=[Activator]::CreateInstance($reserveType)
$dictionary=$reserveType.GetField('_reservesByPartyId',$flags).GetValue($instance)
$read=$reserveType.GetMethod('ReadExistingReserve',$flags)
if($read.Invoke($instance,@('unknown')) -ne -1 -or $dictionary.Count -ne 0){throw 'Observation initialized a reserve or confused unknown with zero'}
$dictionary.Add('party',50)
if($read.Invoke($instance,@('party')) -ne 50 -or $dictionary.Count -ne 1){throw 'Existing reserve not observable'}
$dictionary['party']=0
if($read.Invoke($instance,@('party')) -ne 0){throw 'Known empty reserve lost'}
$api=$logisticsAssembly.GetType('AgesOfCalradiaLogistics.CampaignSystemsApi',$true)
if($api.GetProperty('ApiVersion').GetValue($null,$null) -ne 1){throw 'Unsupported ABI'}
if($api.GetMethod('ReadReserve').Invoke($null,@('party')) -ne -1){throw 'API leaked stale Active behavior outside campaign'}
$coreCandidate=Join-Path $frameworkAuditRoot 'Modules\AgesOfCalradiaCampaignSystems\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'
$coreAssembly=[Reflection.Assembly]::LoadFrom($coreCandidate)
$connection=[Activator]::CreateInstance($coreAssembly.GetType('AgesOfCalradia.CampaignSystems.LogisticsConnection',$true))
$snapshot=$connection.Read('party')
if($snapshot.Status -ne 'untracked-or-no-campaign' -or $snapshot.SupportsWorkshopTransport){throw 'Real logistics discovery or transport capability incorrect'}
'PASS: real Logistics read-only ABI, no phantom reserve, known zero, stale-campaign protection and Core discovery.'
$references+=$coreCandidate
$references+=Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.dll'
$references+=Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\TaleWorlds.Engine.dll'
$references+=Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\TaleWorlds.DotNet.dll'
Add-Type -Path (Join-Path $frameworkAuditRoot 'Modules\AgesOfCalradiaCampaignSystems\Tests\CoreLifecycleFixture.cs') -ReferencedAssemblies $references
[CoreLifecycleFixture]::Run()
