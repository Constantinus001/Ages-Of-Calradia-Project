param([Parameter(Mandatory=$true)][string]$CandidateRun)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$receipt=Get-Content -Raw (Join-Path $CandidateRun 'verification.json')|ConvertFrom-Json
$expectedCounts=@{Player=6;Logistics=5;Diagnostics=3}
$allEntries=@{}
foreach($group in $expectedCounts.Keys){
 $download=$receipt.Downloads.$group
 if((Get-FileHash -LiteralPath $download.Path).Hash -ne $download.Hash){throw 'Download changed from receipt.'}
 $zip=[IO.Compression.ZipFile]::OpenRead($download.Path)
 try {
  if($zip.Entries.Count -ne $expectedCounts[$group]){throw "Unexpected file count: $group"}
  foreach($entry in $zip.Entries){
   if($entry.FullName -eq 'INSTALL.md'){continue}
   if($allEntries.ContainsKey($entry.FullName)){throw 'Duplicate runtime file across downloads.'}
   $allEntries[$entry.FullName]=$group
   if($group -eq 'Player' -and $entry.FullName -match 'SoakDiagnostics|AgesOfCalradiaLogistics'){throw 'Optional module leaked into player archive.'}
   if($entry.FullName -match '/AgesOfCalradia\.dll$|WorldCalendar\.xml$'){throw 'Protected file leaked into download.'}
  }
 }finally{$zip.Dispose()}
}
foreach($relative in @('AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll',
 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll',
 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll')){
 $assembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path (Join-Path $CandidateRun 'package') $relative))
 if(@($assembly.GetReferencedAssemblies()|Where-Object {$_.Name -match 'SoakDiagnostics|AgesOfCalradiaLogistics'}).Count){throw 'Player assembly hard-depends on optional module.'}
}
# Exact-list rejection uses only newly created temporary fixtures.
$fixture=Join-Path $env:TEMP ('aoc-split-'+[guid]::NewGuid().ToString('N'))
$copy=Join-Path $fixture 'package'
New-Item -ItemType Directory -Path $fixture|Out-Null
Copy-Item -LiteralPath (Join-Path $CandidateRun 'package') -Destination $copy -Recurse
$split=Join-Path $CandidateRun 'source\Tests\Split-CampaignSystemsPackage.ps1'
$guide=Join-Path $CandidateRun 'source\docs\CAMPAIGN_SYSTEMS_INSTALL.md'
Copy-Item -LiteralPath $guide -Destination (Join-Path $copy 'unexpected.md')
try { & $split -PackageRoot $copy -Destination (Join-Path $fixture 'unexpected-output') -GuidePath $guide|Out-Null; throw 'Unexpected file accepted.' }
catch {if($_.Exception.Message -notmatch 'exact split allowlist'){throw}}
if(Test-Path -LiteralPath (Join-Path $fixture 'unexpected-output')){throw 'Rejected package produced output.'}
Remove-Item -LiteralPath (Join-Path $copy 'unexpected.md')
Remove-Item -LiteralPath (Join-Path $copy 'AgesOfCalradiaSoakDiagnostics\SubModule.xml')
try { & $split -PackageRoot $copy -Destination (Join-Path $fixture 'missing-output') -GuidePath $guide|Out-Null; throw 'Missing file accepted.' }
catch {if($_.Exception.Message -notmatch 'exact split allowlist'){throw}}
if(Test-Path -LiteralPath (Join-Path $fixture 'missing-output')){throw 'Incomplete package produced output.'}
'PASS: separate downloads, exact counts/hashes, no duplicate runtime payload, no protected artifacts, no hard optional dependencies; extra/missing files rejected before output.'
