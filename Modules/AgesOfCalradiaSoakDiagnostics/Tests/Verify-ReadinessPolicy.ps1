$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CaptureReadinessPolicy.ps1')
$now=[DateTimeOffset]::UtcNow
$id=[guid]::NewGuid().ToString('N')
$r=[pscustomobject]@{schema=1;utc=$now.ToString('O');status='RECORDING';reason='';pid=123;processStartUtc=$now.AddMinutes(-2).ToString('O');requiredHooks=60;session=$id;log='fixture.tsv';bytes=100;campaign='game';discardedRows=0;assemblies=@([pscustomobject]@{name='fixture';mvid='m';diskSha256='h'})}
$builds=@([pscustomobject]@{name='fixture';mvid='m';sha256='h'})
$processes=@([pscustomobject]@{Id=123;StartTime=$now.AddMinutes(-2).UtcDateTime})
$state=@('AOC_FRAMEWORK_LOG_V1',[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('game')),'1','2','100','open')
function Check { param($receipt=$r,$tail=$id,$checkpoint=$state,$procs=$processes)
 @(Get-CaptureReceiptProblems -Receipt $receipt -Builds $builds -Processes $procs -Log 'fixture.tsv' -Length 120 -Checkpoint $checkpoint -TailSession $tail -Now $now)
}
if(@(Check).Count){throw 'Valid receipt rejected'}
foreach($field in @('utc','pid','processStartUtc','requiredHooks','status','discardedRows','session')){
 $copy=$r|ConvertTo-Json -Depth 4|ConvertFrom-Json
 switch($field){
  'utc' {$copy.utc=$now.AddSeconds(-25).ToString('O')}
  'pid' {$copy.pid=124}
  'processStartUtc' {$copy.processStartUtc=$now.AddHours(-1).ToString('O')}
  'requiredHooks' {$copy.requiredHooks=0}
  'status' {$copy.status='BLOCKED';$copy.reason='write failed'}
  'discardedRows' {$copy.discardedRows=1}
  'session' {$copy.session='not-a-session'}
 }
 if(!@(Check -receipt $copy).Count){throw ('Invalid '+$field+' accepted')}
}
$copy=$r|ConvertTo-Json -Depth 4|ConvertFrom-Json;$copy.assemblies[0].mvid='old'
if(!@(Check -receipt $copy).Count){throw 'Loaded old build accepted'}
if(!@(Check -tail 'other').Count){throw 'Wrong log session accepted'}
if(!@(Check -procs @()).Count){throw 'Dead process accepted'}
$state[5]='closed';if(!@(Check).Count){throw 'Closed writer accepted'}
$dir=Join-Path $env:TEMP ('aoc-readiness-policy-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $dir|Out-Null
$path=Join-Path $dir 'tail.tsv'
[IO.File]::WriteAllText($path,((@('utc',$id,'1','10','SESSION_START','0','0','campaign','supply_v7','0','0','detail') -join "`t")+"`npartial-last-line"))
if((Read-CaptureTailSession -Path $path) -ne $id){throw 'Partial last line hid preceding complete session evidence'}
'PASS: valid, stale, PID reuse, old build, wrong session, closed writer, missing hooks and dropped records; partial line is not an integrity failure.'
