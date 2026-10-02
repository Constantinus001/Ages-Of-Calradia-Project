param(
    [string]$ModuleRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$BannerlordDir='C:/Program Files/Steam/steamapps/common/Mount & Blade II Bannerlord'
)
$ErrorActionPreference='Stop'
$bin=Join-Path $BannerlordDir 'bin/Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.Localization','TaleWorlds.Core','TaleWorlds.ObjectSystem','TaleWorlds.CampaignSystem')){
    [void][Reflection.Assembly]::LoadFrom((Join-Path $bin ($name+'.dll')))
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $ModuleRoot 'bin/Win64_Shipping_Client/AgesOfCalradia.SoakDiagnostics.dll'))
$type=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.PeaceEventHistory',$true)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$tracker=$type.GetConstructor($flags,$null,[type[]]@([int]),$null).Invoke(@([int]2))
$war=$type.GetMethod('RecordWar',$flags); $peace=$type.GetMethod('RecordPeace',$flags); $reset=$type.GetMethod('Reset',$flags)
function Field($obj,[string]$name){$obj.GetType().GetField($name,$flags).GetValue($obj)}
function Record-War([string]$a,[string]$b,[double]$day,[double]$seconds){$war.Invoke($tracker,@($a,$b,$day,$seconds,'CausedByKingdomDecision'))}
function Record-Peace([string]$a,[string]$b,[double]$day,[double]$seconds){$peace.Invoke($tracker,@($a,$b,$day,$seconds))}

$unknown=Record-Peace 'a' 'b' 20 50
if((Field $unknown 'TimingKnown') -or (Field $unknown 'RapidReversal')){throw 'Unobserved war must not become zero-duration peace.'}
[void](Record-War 'a' 'b' 20 50)
$rapid=Record-Peace 'b' 'a' 20 50.0015
if(-not (Field $rapid 'RapidReversal') -or [Math]::Abs((Field $rapid 'ElapsedSeconds')-0.0015) -gt 0.000001){throw 'Reversed pair or monotonic duration failed.'}
$duplicate=Record-Peace 'a' 'b' 20 50.002
if(-not (Field $duplicate 'DuplicatePeace') -or (Field $duplicate 'RapidReversal')){throw 'Duplicate peace incorrectly counted as another reversal.'}
[void](Record-War 'b' 'a' 21 60)
$normal=Record-Peace 'a' 'b' 24 180
if((Field $normal 'RapidReversal') -or (Field $normal 'DuplicatePeace') -or (Field $normal 'ElapsedDays') -ne 3){throw 'Redeclaration did not reset the pair.'}
[void](Record-War 'a' 'c' 25 190)
$rewind=Record-Peace 'a' 'c' 24 191
if(Field $rewind 'TimingKnown'){throw 'Campaign rewind must invalidate correlation timing.'}
[void](Record-War 'c' 'd' 30 210)
if(Field (Record-Peace 'a' 'b' 31 220) 'TimingKnown'){throw 'Bounded history did not evict the oldest pair.'}
[void]$reset.Invoke($tracker,@())
if(Field (Record-Peace 'c' 'd' 31 220) 'TimingKnown'){throw 'Reload reset reused old session timing.'}
if((Record-War 'a' 'b' ([double]::NaN) 10) -ne 0 -or (Record-War 'a' 'a' 10 10) -ne 0){throw 'Invalid observation was tracked.'}
[void](Record-War 'a' 'b' 20 50)
if(Field (Record-Peace 'a' 'b' 20 ([double]::PositiveInfinity)) 'TimingKnown'){throw 'Nonfinite timing accepted.'}
[void]$reset.Invoke($tracker,@())
[void](Record-War 'a|b' 'c' 20 50)
if(Field (Record-Peace 'a' 'b|c' 20 50) 'TimingKnown'){throw 'Faction ID delimiter collision.'}

$event=[TaleWorlds.CampaignSystem.CampaignEvents].GetProperty('MakePeace').PropertyType.GetGenericArguments()
if($event.Count -ne 3 -or $event[2].FullName -ne 'TaleWorlds.CampaignSystem.Actions.MakePeaceAction+MakePeaceDetail'){throw 'Native peace event signature changed.'}
$field=[TaleWorlds.CampaignSystem.FactionManager].GetField('_stances',$flags)
if(-not $field -or -not $field.FieldType.GetMethod('GetStance',$flags,$null,[type[]]@([TaleWorlds.CampaignSystem.IFaction],[TaleWorlds.CampaignSystem.IFaction]),$null)){throw 'Read-only native stance lookup unavailable.'}
$source=Get-Content -Raw -LiteralPath (Join-Path $ModuleRoot 'PeaceEventDiagnostics.cs')
foreach($contract in @('Stopwatch.GetTimestamp()','PEACE_DIAGNOSTIC','PEACE_RAPID_REVERSAL','CalendarSoakBehavior.IsArmed','GetDailyTributeToPay','callerStack=', 'Take(24)','history=session-only')){
    if(-not $source.Contains($contract)){throw "Missing peace diagnostic contract: $contract"}
}
if($source -match 'MakePeaceAction\.Apply|DeclareWarAction\.Apply|SetNeutral\(|\.GetStanceWith\(|HarmonyPatch|SaveableField'){
    throw 'Observer must not mutate diplomacy, create stances, patch the game or add saved state.'
}
Write-Output 'PASS: peace correlation, reversed pairs, duplicates, redeclaration, reload reset, eviction, invalid clocks and native read-only event contract.'
