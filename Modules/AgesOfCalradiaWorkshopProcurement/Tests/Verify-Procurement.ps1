$ErrorActionPreference='Stop'
$module=Split-Path $PSScriptRoot
$framework=Join-Path $module '..\AgesOfCalradiaCampaignSystems'
Add-Type -Path @((Join-Path $module 'ProcurementState.cs'),(Join-Path $module 'ProcurementTransfer.cs'),(Join-Path $framework 'ProcurementSettings.cs'),(Join-Path $framework 'EconomicTransfer.cs'),(Join-Path $PSScriptRoot 'ProcurementVerifier.cs')) -ReferencedAssemblies @('System','System.Core','System.Runtime.Serialization','System.Xml','System.Xml.Linq')
[ProcurementVerifier]::Run()
