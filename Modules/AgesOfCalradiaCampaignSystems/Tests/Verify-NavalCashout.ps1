param([string]$AssemblyPath=(Join-Path (Split-Path $PSScriptRoot) 'bin/Win64_Shipping_Client/AgesOfCalradia.CampaignSystems.dll'))
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Run this native Harmony fixture with powershell.exe (Windows PowerShell).'}
$game='C:/Program Files/Steam/steamapps/common/Mount & Blade II Bannerlord'
$refs=@('System','System.Core','System.Xml','System.Xml.Linq')
foreach($name in @('TaleWorlds.Library','TaleWorlds.LinQuick','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 $p=Join-Path $game ('bin/Win64_Shipping_Client/'+$name+'.dll')
 [Reflection.Assembly]::LoadFrom($p)|Out-Null
 $refs+=$p
}
$h=Join-Path $env:USERPROFILE '.nuget/packages/lib.harmony/2.4.2/lib/net472/0Harmony.dll'
[Reflection.Assembly]::LoadFrom($h)|Out-Null
$refs+=$h
$refs+='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
[Reflection.Assembly]::LoadFrom((Join-Path $game 'Modules/NavalDLC/bin/Win64_Shipping_Client/NavalDLC.dll'))|Out-Null
Add-Type -Path @((Join-Path $PSScriptRoot 'NavalCashoutVerifier.cs'),(Join-Path $PSScriptRoot 'NavalCashoutEndToEnd.cs')) -ReferencedAssemblies $refs
try { [NavalCashoutVerifier]::Run((Resolve-Path -LiteralPath $AssemblyPath).Path) }
catch { Write-Output $_.Exception.ToString(); throw }
