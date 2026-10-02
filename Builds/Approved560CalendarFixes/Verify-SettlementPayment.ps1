param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'),
 [string]$SidecarPath=(Join-Path $PSScriptRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-Approved560CalendarFixes.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath -SidecarPath $SidecarPath
$type=$sidecar.GetType('AgesOfCalradia.Approved560CalendarFixes.SettlementPaymentConservationFix',$true)
$target=$type.GetMethod('TargetMethod',[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,@())
if(-not ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners -contains $owner)){throw 'Settlement payment fix not installed'}
$references=@($HarmonyPath,[object].Assembly.Location,[System.Linq.Enumerable].Assembly.Location)
$references+='C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Facades\netstandard.dll'
foreach($name in @('TaleWorlds.Library','TaleWorlds.Core','TaleWorlds.CampaignSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem')){
 $references+=Join-Path $BannerlordDir ('bin\Win64_Shipping_Client\'+$name+'.dll')
}
Add-Type -Path (Join-Path $PSScriptRoot 'NativeSettlementPaymentFixture.cs') -ReferencedAssemblies $references
[NativeSettlementPaymentFixture]::Run()
'LIMIT: event delivery is stubbed; native transfer and wallet mutations execute. This does not change sale quantities or create escrow for unpaid goods.'
