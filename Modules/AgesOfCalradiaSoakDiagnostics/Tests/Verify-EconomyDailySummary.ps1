param([string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
$moduleRoot=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.DotNet','TaleWorlds.ScreenSystem','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.SaveSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')){
    [Reflection.Assembly]::LoadFrom((Join-Path $bin ($name+'.dll')))|Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))|Out-Null
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $moduleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$static=[Reflection.BindingFlags]'Static,NonPublic'
$type=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyDailySummary',$true)
$limit=$type.GetField('SessionByteLimit',$static).GetValue($null)
if($limit -ne 5GB){throw "Summary session cap must be 5 GiB; got $limit"}
$root=Join-Path $env:TEMP ('aoc-daily-summary-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$path=Join-Path $root 'daily.tsv'
$writer=$type.GetConstructor($flags,$null,[type[]]@([string]),$null).Invoke(@([string]$path))
$add=$type.GetMethod('Add',$flags)
function Add-Row([double]$day,[string]$kind,[string]$owner,[string]$metric,[double]$before,[double]$after){
    $add.Invoke($writer,@($day,$kind,$owner,$metric,'native.fixture',$before,$after,'detail'))|Out-Null
}
try {
    Add-Row 1.1 BEGIN ignored scope 0 0
    Add-Row 1.2 HERO_GOLD hero:a gold 100 90
    Add-Row 1.3 HERO_GOLD hero:a gold 90 100
    Add-Row 1.4 CLAN_SETTLEMENT hero:a net 100 100
    Add-Row 1.5 INVENTORY roster:1 'item:grain/modifier:a' 10 8
    Add-Row 1.6 INVENTORY roster:2 'item:grain/modifier:b' 0 2
    Add-Row 2.1 HERO_GOLD hero:a gold 100 105
    $rejected=$false
    try{Add-Row 2.2 HERO_GOLD hero:a gold 0 ([double]::NaN)}catch{$rejected=$true}
    if(-not $rejected){throw 'Nonfinite aggregate accepted'}
} finally {$writer.Dispose()}
$rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
$gold=@($rows|Where-Object {$_.kind -eq 'HERO_GOLD' -and $_.day -eq '1'})
if($gold.Count -ne 1 -or $gold[0].count -ne '2' -or $gold[0].net -ne '0' -or $gold[0].positive -ne '10' -or $gold[0].negative -ne '-10'){throw 'Offsetting gross flows lost or double-counted'}
if(@($rows|Where-Object kind -eq BEGIN).Count){throw 'Raw scope leaked into summary'}
if(@($rows|Where-Object kind -eq CLAN_SETTLEMENT).Count -ne 1){throw 'Zero net settlement was lost'}
$inventory=@($rows|Where-Object kind -eq INVENTORY)
if($inventory.Count -ne 1 -or $inventory[0].metric -ne 'item:grain' -or $inventory[0].net -ne '0'){throw 'Inventory aggregation cardinality contract failed'}
if(@($rows|Where-Object {$_.day -eq '2' -and $_.net -eq '5'}).Count -ne 1){throw 'Day rollover lost data'}
# Validate the summary gate with clearly synthetic generated evidence.
$path=Join-Path $root 'AocEconomyDaily-fixture.tsv'
$writer=$type.GetConstructor($flags,$null,[type[]]@([string]),$null).Invoke(@([string]$path))
try {
    Add-Row 1 SESSION_START fixture schema 0 0
    foreach($kind in @('MARKET_STOCK','RECIPE_DEFINITION','RECIPE_CYCLE','RECIPE_PROGRESS','RECIPE_SPEED','VILLAGE_PRODUCTION','CLAN_SETTLEMENT','WALLET','ECONOMY_PACING')){Add-Row 1 $kind fixture metric 0 0}
    $add.Invoke($writer,@([double]1,'STATE_COVERAGE','fixture','towns','fixture',[double]57,[double]57,'complete=True'))|Out-Null
    Add-Row 1 SESSION_END fixture end 0 0
} finally {$writer.Dispose()}
[IO.File]::WriteAllText((Join-Path $root 'AocSoakEvents.tsv'),'synthetic fixture only')
$gate=Join-Path $PSScriptRoot 'Verify-EconomyDailyCoverage.ps1'
& $gate -DiagnosticsDirectory $root|Out-Null
$fixtureRows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
$fixtureRows|Where-Object kind -ne SESSION_END|Export-Csv -LiteralPath $path -Delimiter "`t" -NoTypeInformation
$rejected=$false
try{& $gate -DiagnosticsDirectory $root|Out-Null}catch{if($_.Exception.Message -match 'Incomplete daily session'){$rejected=$true}else{throw}}
if(-not $rejected){throw 'Summary gate accepted partial session'}
# Bound cardinality without generating per-call disk output.
$limitPath=Join-Path $root 'limit.tsv'
$writer=$type.GetConstructor($flags,$null,[type[]]@([string]),$null).Invoke(@([string]$limitPath))
try {
    for($i=0;$i -lt 50000;$i++){Add-Row 1 WALLET ('owner:'+ $i) gold 0 1}
    $rejected=$false
    try{Add-Row 1 WALLET overflow gold 0 1}catch{if($_.Exception.ToString() -match 'key limit'){$rejected=$true}else{throw}}
    if(-not $rejected){throw 'Daily key budget did not reject overflow'}
} finally {$writer.Dispose()}
# Patch actual installed native IL in this isolated verifier process. No campaign starts.
$patches=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.EconomyDiagnosticPatches',$true)
try {
    $patches.GetMethod('Install',$static).Invoke($null,@())|Out-Null
    if(-not $patches.GetProperty('Ready',$static).GetValue($null)){throw 'Native observer IL patch installation failed'}
    foreach($target in @($patches.GetMethod('Targets',$static).Invoke($null,@()))){
        $info=[HarmonyLib.Harmony]::GetPatchInfo($target)
        $prefix=@($info.Prefixes|Where-Object owner -eq 'aoc.soak.economy.observer.v1')[0]
        foreach($postfix in @($info.Postfixes|Where-Object owner -eq 'aoc.soak.economy.observer.v1')){
            if($postfix.PatchMethod.DeclaringType -ne $prefix.PatchMethod.DeclaringType){throw 'Production postfix cannot receive Harmony prefix state across declaring types'}
        }
    }
} finally {$patches.GetMethod('Uninstall',$static).Invoke($null,@())|Out-Null}
Write-Output "PASS: bounded summary grouping, gross/net offsets, zero-net records, day rollover, invalid-number rejection and actual native patch installation. Synthetic output: $root"
Write-Output 'NOT_EXERCISED: campaign runtime overhead, production branch coverage, cash attribution and long-run volume.'
