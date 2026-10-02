param([Parameter(Mandatory=$true)][string]$CandidateRun,[Parameter(Mandatory=$true)][string]$BaselineRoot)
$ErrorActionPreference='Stop'
$validator=Join-Path $CandidateRun 'source\Tests\Verify-CampaignSystemsPackage.ps1'
$fixture=Join-Path $env:TEMP ('aoc-package-negative-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture|Out-Null
$package=Join-Path $fixture 'package'
Copy-Item -LiteralPath (Join-Path $CandidateRun 'package') -Destination $package -Recurse
function Reject([string]$Pattern){
 $rejected=$false
 try{& $validator -PackageRoot $package -BaselineRoot $BaselineRoot|Out-Null}
 catch{if($_.Exception.Message -match $Pattern){$rejected=$true}else{throw}}
 if(-not $rejected){throw "Package accepted invalid fixture: $Pattern"}
}
& $validator -PackageRoot $package -BaselineRoot $BaselineRoot|Out-Null
$manifest=Join-Path $package 'AOC CORE\SubModule.xml'
$original=Join-Path $CandidateRun 'package\AOC CORE\SubModule.xml'
$duplicate=Join-Path $package 'AgesOfCalradiaWorkshopProcurement\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll'
New-Item -ItemType Directory -Path (Split-Path $duplicate) -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $package 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll') -Destination $duplicate
Reject 'exactly once'
Remove-Item -LiteralPath $duplicate # Exact newly-created fixture copy, not a user artifact.
[xml]$xml=Get-Content -Raw $manifest
$xml.Module.Name.value='Changed existing Core identity'
$xml.Save($manifest)
Reject 'alter existing Core'
Copy-Item -LiteralPath $original -Destination $manifest
[xml]$xml=Get-Content -Raw $manifest
$node=@($xml.Module.SubModules.SubModule|Where-Object {$_.DLLName.value -eq 'AgesOfCalradiaBorderEditPrototype.dll'})
if($node.Count -ne 1){throw 'Expected installed border integration for preservation regression'}
$node[0].ParentNode.RemoveChild($node[0])|Out-Null
$xml.Save($manifest)
Reject 'alter existing Core'
Copy-Item -LiteralPath $original -Destination $manifest
$consumer=Join-Path $package 'AgesOfCalradiaLogistics\SubModule.xml'
[xml]$xml=Get-Content -Raw $consumer
$dependency=@($xml.Module.DependedModules.DependedModule|Where-Object {$_.Id -eq 'AgesOfCalradia'})[0]
$dependency.ParentNode.RemoveChild($dependency)|Out-Null
$xml.Save($consumer)
Reject 'Missing Core ordering dependency'
"PASS: rejected duplicate framework, changed Core identity, lost border registration and missing consumer dependency. Isolated fixture: $fixture"
