param(
 [string]$DiagnosticsAssemblyPath=(Join-Path $PSScriptRoot '..\..\..\output\procurement-diagnostics\AgesOfCalradia.SoakDiagnostics.dll'),
 [string]$LogisticsAssemblyPath=(Join-Path $PSScriptRoot '..\..\..\output\campaign-systems-logistics\AgesOfCalradiaLogistics.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$logistics=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $LogisticsAssemblyPath).Path)
$references=@('System','System.Core','System.Runtime.Serialization',$harmonyPath)
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
 $references+=Join-Path $gameBin ($name+'.dll')
}
$references+='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
Add-Type -Path (Join-Path $PSScriptRoot 'DiagnosticExtensionsFixture.cs') -ReferencedAssemblies $references
[DiagnosticExtensionsFixture]::Run($assembly,$logistics,$testRoot)
"Synthetic evidence only: $testRoot"
