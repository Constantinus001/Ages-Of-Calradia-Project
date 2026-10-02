param(
 [Parameter(Mandatory=$true)][string]$PackageRoot,
 [Parameter(Mandatory=$true)][string]$Destination,
 [Parameter(Mandatory=$true)][string]$GuidePath
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $Destination){throw 'Split destination must not already exist.'}
$groups=[ordered]@{
 Player=@('AOC CORE\SubModule.xml','AOC CORE\CampaignSystems.example.xml',
 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.CampaignSystems.dll',
 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.Approved560CalendarFixes.dll',
 'AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.WorkshopProcurement.dll')
 Logistics=@('AgesOfCalradiaLogistics\SubModule.xml',
 'AgesOfCalradiaLogistics\bin\Win64_Shipping_Client\AgesOfCalradiaLogistics.dll',
 'AgesOfCalradiaLogistics\ModuleData\supply_items.xml','AgesOfCalradiaLogistics\ModuleData\module_strings.xml')
 Diagnostics=@('AgesOfCalradiaSoakDiagnostics\SubModule.xml',
 'AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll')
}
$root=(Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\')
$allowed=@($groups.Values|ForEach-Object {$_})
$actual=@(Get-ChildItem -LiteralPath $root -File -Recurse|ForEach-Object {$_.FullName.Substring($root.Length+1)})
if(@(Compare-Object $allowed $actual).Count){throw 'Package differs from the exact split allowlist.'}
if(-not(Test-Path -LiteralPath $GuidePath -PathType Leaf)){throw 'Installation guide missing.'}
$result=[ordered]@{}
foreach($group in $groups.Keys){
 $archive=Join-Path $Destination ("CampaignSystems-$group-candidate.zip")
 New-Item -ItemType Directory -Path $Destination -Force|Out-Null
 $zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
 $expected=@{}
 try {
  foreach($relative in $groups[$group]){
   $source=Join-Path $root $relative
   $entryName='Modules/'+$relative.Replace('\','/')
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$source,$entryName)|Out-Null
   $expected[$entryName]=(Get-FileHash -LiteralPath $source).Hash
  }
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$GuidePath,'INSTALL.md')|Out-Null
  $expected['INSTALL.md']=(Get-FileHash -LiteralPath $GuidePath).Hash
 }finally{$zip.Dispose()}
 $zip=[IO.Compression.ZipFile]::OpenRead($archive)
 try {
  if($zip.Entries.Count -ne $expected.Count){throw 'Split archive entry count mismatch.'}
  $seen=@{}
  foreach($entry in $zip.Entries){
   if(-not $expected.ContainsKey($entry.FullName) -or $seen.ContainsKey($entry.FullName)){throw 'Unexpected or duplicate split entry.'}
   $seen[$entry.FullName]=$true
   $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$sha.Dispose();$stream.Dispose()}
   if($hash -ne $expected[$entry.FullName]){throw 'Split archive content hash mismatch.'}
  }
 }finally{$zip.Dispose()}
 $result[$group]=[ordered]@{Path=$archive;Hash=(Get-FileHash -LiteralPath $archive).Hash;Files=$expected}
}
# Caller owns scanning and acceptance. Splitting cannot promote a private delta.
$result
