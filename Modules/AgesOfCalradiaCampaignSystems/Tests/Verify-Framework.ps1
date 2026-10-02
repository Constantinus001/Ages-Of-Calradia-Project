$ErrorActionPreference='Stop'
$module=Split-Path $PSScriptRoot
$files=@('CampaignSystems.cs','ProcurementSettings.cs','EconomicTransfer.cs','LogisticsConnection.cs') | ForEach-Object { Join-Path $module $_ }
$files+=Join-Path $PSScriptRoot 'FrameworkVerifier.cs'
Add-Type -Path $files -ReferencedAssemblies @('System','System.Core','System.Xml','System.Xml.Linq')
[FrameworkVerifier]::Run()
$combined=[AgesOfCalradia.CampaignSystems.ProcurementSettings]::Load((Join-Path $module 'CampaignSystems.combined-economy-candidate.xml'))
if($combined.IronSupplierReserveDays -ne 14 -or $combined.MaximumAdaptiveBatches -ne 12 -or $combined.DeliveryDelayCostWeight -ne 1 -or $combined.WineExpenseCoverage -ne 1.25){throw 'Combined candidate configuration does not match reviewed policy'}
'PASS: actual combined candidate XML parsed; policy revision '+$combined.Revision
