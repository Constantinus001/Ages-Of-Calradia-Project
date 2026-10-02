param([string]$CoreModule = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE')
$ErrorActionPreference='Stop'
function Assert-True([bool]$condition,[string]$message) { if(!$condition){throw $message} }
$bin=Join-Path $CoreModule 'bin\Win64_Shipping_Client';$assets=Join-Path $CoreModule 'AuthoredBorders'
[xml]$manifest=Get-Content -LiteralPath (Join-Path $CoreModule 'SubModule.xml') -Raw
$published=$manifest.SelectSingleNode('//SubModule[SubModuleClassType/@value="Aoc.BorderEditPrototype.PublishedBorderSubModule"]')
$optimizer=$manifest.SelectSingleNode('//SubModule[SubModuleClassType/@value="AgesOfCalradia.PoliticalBorderOptimizer.PoliticalBorderOptimizerSubModule"]')
Assert-True ($published -ne $null -and $optimizer -ne $null) 'Published Borders and optimizer must both be registered.'
$modules=@($manifest.Module.SubModules.SubModule);$publishedIndex=[array]::IndexOf($modules,$published);$optimizerIndex=[array]::IndexOf($modules,$optimizer)
Assert-True ($publishedIndex -ge 0 -and $optimizerIndex -gt $publishedIndex) 'Published Borders must load before the optimizer.'
$topology='F1B414DB1DC9677436F1AADA3E93606DBEDEB60F52E797417C379265B399FDF9';$layout=Join-Path $assets ($topology+'.xml');$repair=Join-Path $assets 'Repair.xml'
Assert-True ((Test-Path -LiteralPath $layout) -and (Test-Path -LiteralPath $repair)) 'Published layout or reviewed fill repair is missing.'
[xml]$layoutXml=Get-Content -LiteralPath $layout -Raw;[xml]$repairXml=Get-Content -LiteralPath $repair -Raw
$layoutHash=(Get-FileHash -LiteralPath $layout -Algorithm SHA256).Hash
Assert-True ($layoutXml.DocumentElement.GetAttribute('topology') -eq $topology -and -not $layoutXml.DocumentElement.HasAttribute('layoutIdentity') -and @($layoutXml.DocumentElement.Bridge).Count -eq 371) 'Installed published layout is not the reviewed 371-bridge F1B topology.'
Assert-True ($layoutHash -eq '646FEEE68ED46DE6434E4E55157A5C1C370203F25651E1C6956497CE94052507') 'Installed published layout hash differs from the reviewed published F1B source.'
Assert-True ($repairXml.DocumentElement.GetAttribute('topology') -eq $topology -and $repairXml.DocumentElement.GetAttribute('draftHash') -eq $layoutHash -and @($repairXml.DocumentElement.Row).Count -eq 25 -and [int]$repairXml.DocumentElement.GetAttribute('regions') -eq 42) 'Repair.xml is not bound to the reviewed F1B layout.'
foreach($name in @('AgesOfCalradiaBorderEditPrototype.dll','AgesOfCalradia.PoliticalBorderOptimizer.dll','AgesOfCalradia.CampaignLabelVisibility.dll')) { Assert-True (Test-Path -LiteralPath (Join-Path $bin $name)) "Missing deployed sidecar: $name" }
foreach($candidate in @(
 (Join-Path $PSScriptRoot 'BorderEditPrototype\package\AgesOfCalradiaBorderEditPrototype\bin\Win64_Shipping_Client\AgesOfCalradiaBorderEditPrototype.dll'),
 (Join-Path $PSScriptRoot '..\tmp\political-border-optimizer\bin\Win64_Shipping_Client\AgesOfCalradia.PoliticalBorderOptimizer.dll')
)) {
 Assert-True (Test-Path -LiteralPath $candidate) "Reviewed build is missing: $candidate"
 $installed=Join-Path $bin (Split-Path -Leaf $candidate)
 Assert-True ((Get-FileHash -LiteralPath $installed).Hash -eq (Get-FileHash -LiteralPath $candidate).Hash) "Installed sidecar differs from the reviewed build: $installed"
}
Write-Output 'PASS: installed published 371-bridge layout, 42-region repair, and sidecar manifest ordering are exact.'
