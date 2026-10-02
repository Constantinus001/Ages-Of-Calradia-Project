param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$ModuleRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
 [string]$HarmonyPath=(Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'),
 [switch]$WithDiagnostics,
 [string]$DiagnosticsPath='',
 [string]$SidecarPath=(Join-Path $PSScriptRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-WorkshopApprovalQuote.ps1') -BannerlordDir $BannerlordDir -ModuleRoot $ModuleRoot -HarmonyPath $HarmonyPath -SidecarPath $SidecarPath
$references=@($HarmonyPath)
$references+=[object].Assembly.Location
$references+=[System.Linq.Enumerable].Assembly.Location
$references+='C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Facades\netstandard.dll'
foreach($name in @('TaleWorlds.Library','TaleWorlds.Core','TaleWorlds.CampaignSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem')){
 $references+=Join-Path $BannerlordDir ('bin\Win64_Shipping_Client\'+$name+'.dll')
}
Add-Type -Path (Join-Path $PSScriptRoot 'NativeWorkshopBatchFixture.cs') -ReferencedAssemblies $references
$diagnosticObserver=$null
if($WithDiagnostics){
 $sourceRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
 if(-not $DiagnosticsPath){$DiagnosticsPath=Join-Path $sourceRoot 'Modules\AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'}
 $diagnosticAssembly=[Reflection.Assembly]::LoadFrom($DiagnosticsPath)
 $diagnosticObserver=$diagnosticAssembly.GetType('AgesOfCalradia.SoakDiagnostics.SupplyChainObserver',$true)
}
try {
 $install=$null
 if($diagnosticObserver){$install=[Action]{$diagnosticObserver.GetMethod('Install',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())|Out-Null}}
 [NativeWorkshopBatchFixture]::Run($install)
}
catch { Write-Output $_.Exception.ToString(); throw }
finally {if($diagnosticObserver){$diagnosticObserver.GetMethod('Uninstall',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())|Out-Null}}
'LIMIT: campaign setup, prices, random selection, warehouse capacity, skill awards and event dispatch are deterministic boundaries; no save or live game loaded. Combined mode installs all observers but does not certify live capture coverage.'
