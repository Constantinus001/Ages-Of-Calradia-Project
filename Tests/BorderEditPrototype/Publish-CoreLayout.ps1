param(
 [Parameter(Mandatory=$true)][string]$Draft,
 [string]$CoreModule='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE'
)
$ErrorActionPreference='Stop'
$repository=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& (Join-Path $repository 'Tests\Verify-ProtectedPoliticalBaseline.ps1')
$dll='AgesOfCalradiaBorderEditPrototype.dll'
$source=Join-Path $PSScriptRoot ('package\AgesOfCalradiaBorderEditPrototype\bin\Win64_Shipping_Client\'+$dll)
$repair=Join-Path $PSScriptRoot 'Repair.xml'
[xml]$plan=Get-Content -LiteralPath $repair -Raw
[xml]$draftXml=Get-Content -LiteralPath $Draft -Raw
$topology=$draftXml.DocumentElement.GetAttribute('topology')
if($topology -notmatch '^[A-F0-9]{64}$'){throw 'Invalid topology.'}
# The plan is reviewed against one exact draft. Never publish a stale repair.
if((Get-FileHash -LiteralPath $Draft).Hash -ne $plan.DocumentElement.GetAttribute('draftHash')){throw 'Draft does not match reviewed repair.'}
$manifest=Join-Path $CoreModule 'SubModule.xml'
[xml]$module=Get-Content -LiteralPath $manifest -Raw
if(!$module.SelectSingleNode('//SubModule[DLLName/@value="AgesOfCalradia.PoliticalFillSeamFix.dll"]')){throw 'Verified fill seam integration is required.'}
if((Get-FileHash -LiteralPath (Join-Path $CoreModule 'bin\Win64_Shipping_Client\AgesOfCalradia.dll')).Hash -ne '560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E'){throw 'Unsupported Core renderer.'}
if(Get-Process Bannerlord -ErrorAction SilentlyContinue){throw 'Close Bannerlord before publishing.'}
$backup=Join-Path $PSScriptRoot ('.artifacts\before-core-layout-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $manifest -Destination $backup
$targetDll=Join-Path $CoreModule ('bin\Win64_Shipping_Client\'+$dll)
if(Test-Path -LiteralPath $targetDll){Copy-Item -LiteralPath $targetDll -Destination $backup}
$assets=Join-Path $CoreModule 'AuthoredBorders'
if(Test-Path -LiteralPath $assets){Copy-Item -LiteralPath $assets -Destination $backup -Recurse}
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item -LiteralPath $Draft -Destination (Join-Path $assets ($topology+'.xml'))
Copy-Item -LiteralPath $repair -Destination (Join-Path $assets 'Repair.xml')
Copy-Item -LiteralPath $source -Destination $targetDll
if(!$module.SelectSingleNode('//SubModule[SubModuleClassType/@value="Aoc.BorderEditPrototype.PublishedBorderSubModule"]')){
 $entry=$module.CreateElement('SubModule')
 foreach($pair in @(@('Name','AOC CORE: Published Borders'),@('DLLName',$dll),@('SubModuleClassType','Aoc.BorderEditPrototype.PublishedBorderSubModule'))){
  $child=$module.CreateElement($pair[0]);$child.SetAttribute('value',$pair[1]);[void]$entry.AppendChild($child)
 }
 $tags=$module.CreateElement('Tags')
 foreach($pair in @(@('DedicatedServerType','none'),@('IsNoRenderModeElement','false'))){$tag=$module.CreateElement('Tag');$tag.SetAttribute('key',$pair[0]);$tag.SetAttribute('value',$pair[1]);[void]$tags.AppendChild($tag)}
 [void]$entry.AppendChild($tags);[void]$module.Module.SubModules.AppendChild($entry)
 $module.Save($manifest)
}
foreach($pair in @(@($source,$targetDll),@($Draft,(Join-Path $assets ($topology+'.xml'))),@($repair,(Join-Path $assets 'Repair.xml')))){
 if((Get-FileHash -LiteralPath $pair[0]).Hash -ne (Get-FileHash -LiteralPath $pair[1]).Hash){throw 'Installed file verification failed.'}
}
& (Join-Path $repository 'Tests\Verify-ProtectedPoliticalBaseline.ps1')
Write-Output "Published saved geometry and repair inside Core. Backup: $backup"
