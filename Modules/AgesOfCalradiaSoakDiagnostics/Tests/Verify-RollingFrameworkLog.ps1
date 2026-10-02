param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$register=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.AgesOfCalradiaSoakDiagnosticsSubModule',$true).GetMethod('ShouldRegister',$flags)
foreach($mask in 0..15){
 $pacing=[bool]($mask-band 1);$soak=[bool]($mask-band 2);$oneShot=[bool]($mask-band 4);$rollingMarker=[bool]($mask-band 8)
 if($register.Invoke($null,@($pacing,$soak,$oneShot,$rollingMarker)) -ne (-not $pacing -and ($soak -or $oneShot -or $rollingMarker))){throw 'Campaign registration marker contract failed'}
}
$rolling=$capture.GetMethod('BeginRollingSession',$flags)
Add-Type -TypeDefinition 'public static class RollingFixtureClock { public static double Day = 42.5; }'
$clock=[Func[double]]{[RollingFixtureClock]::Day}
$path=$rolling.Invoke($null,@([string]$testRoot,$clock))
$write.Invoke($null,@('OLD_PERIOD',[long]0,[long]0,'fixture','old',[double]0,[double]0,''))|Out-Null
$write.Invoke($null,@('WALLET_CHECK',[long]0,[long]0,'rotation_fixture','Gold',[double]1,[double]2,''))|Out-Null
$incidentFiles=@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'AocIncidents') -Filter '*.json'|Where-Object {(Get-Content -LiteralPath $_.FullName -Raw) -match 'rotation_fixture'})
if($incidentFiles.Count -ne 1){throw 'Rotation incident not captured before overwrite'}
$capture.GetField('_startTicks',$flags).SetValue($null,[long]([Diagnostics.Stopwatch]::GetTimestamp()-([Diagnostics.Stopwatch]::Frequency*3601)))
$capture.GetMethod('Tick',$flags).Invoke($null,@())|Out-Null
if(-not $capture.GetProperty('Active',$flags).GetValue($null,$null)){throw 'Wall time incorrectly expired rolling capture'}
[RollingFixtureClock]::Day=72.4999
$capture.GetMethod('Tick',$flags).Invoke($null,@())|Out-Null
$writer=$capture.GetField('_writer',$flags).GetValue($null);$writer.Flush()
if((Get-Content -Raw -LiteralPath $path) -notmatch 'OLD_PERIOD'){throw 'Rotated before 30 days'}
[RollingFixtureClock]::Day=72.5
$capture.GetMethod('Tick',$flags).Invoke($null,@())|Out-Null
$incident=Get-Content -LiteralPath $incidentFiles[0].FullName -Raw|ConvertFrom-Json
if($incident.firstDivergence -notmatch 'rotation_fixture' -or $incident.status -notmatch 'post_window_incomplete'){throw 'Rotation lost or falsely completed incident evidence'}
if(-not $capture.GetProperty('Active',$flags).GetValue($null,$null)){throw 'Rollover stopped capture'}
$rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
if($rows.Count -ne 1 -or $rows[0].kind -ne 'SESSION_START' -or $rows[0].sequence -ne '1'){throw 'Rollover retained old rows or sequence'}
$session=$rows[0].session
[RollingFixtureClock]::Day=102.5
$capture.GetMethod('Tick',$flags).Invoke($null,@())|Out-Null
$rows=@(Import-Csv -LiteralPath $path -Delimiter "`t")
if($rows[0].session -eq $session){throw 'Second rollover reused identity'}
$capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
$before=Get-Content -Raw -LiteralPath $path
[RollingFixtureClock]::Day=103.5
$resumed=$rolling.Invoke($null,@([string]$testRoot,$clock))
if($resumed -ne $path -or $capture.GetField('_startDay',$flags).GetValue($null) -ne 102.5){throw 'Reload reset period/path'}
if((Get-Content -Raw -LiteralPath $path) -ne $before){throw 'Reload erased prior log segment'}
$write.Invoke($null,@('RESUMED',[long]0,[long]0,'fixture','new_segment',[double]0,[double]0,''))|Out-Null
[RollingFixtureClock]::Day=103.75
$write.Invoke($null,@('BETWEEN_TICKS',[long]0,[long]0,'fixture','mutation',[double]0,[double]0,''))|Out-Null
$capture.GetMethod('Stop',$flags).Invoke($null,@('fixture_completed'))|Out-Null
if([double](Get-Content -LiteralPath ($path+'.state'))[3] -ne 103.75){throw 'Checkpoint lost observation time between ticks'}
$hash=(Get-FileHash -LiteralPath $path).Hash
[RollingFixtureClock]::Day=103.6
$rejected=$false
try{$rolling.Invoke($null,@([string]$testRoot,$clock))|Out-Null}catch{if($_.Exception.ToString() -match 'Campaign rollback'){$rejected=$true}else{throw}}
if(-not $rejected -or (Get-FileHash -LiteralPath $path).Hash -ne $hash){throw 'Rollback erased or mixed log'}
$store=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.RollingLogStore',$true)
$ctor=$store.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$rejected=$false
try{$ctor.Invoke(@([string]$testRoot,'different-campaign',[double]104))|Out-Null}catch{if($_.Exception.ToString() -match 'Different campaign'){$rejected=$true}else{throw}}
if(-not $rejected -or (Get-FileHash -LiteralPath $path).Hash -ne $hash){throw 'Different campaign mixed log'}
[RollingFixtureClock]::Day=104
$rolling.Invoke($null,@([string]$testRoot,$clock))|Out-Null
$capture.GetMethod('Fail',$flags).Invoke($null,@('synthetic failure',[Exception]::new('fixture')))|Out-Null
$hash=(Get-FileHash -LiteralPath $path).Hash
$rejected=$false
try{$rolling.Invoke($null,@([string]$testRoot,$clock))|Out-Null}catch{if($_.Exception.ToString() -match 'Interrupted framework period'){$rejected=$true}else{throw}}
if(-not $rejected -or (Get-FileHash -LiteralPath $path).Hash -ne $hash){throw 'Failed period resumed or erased'}
if((Get-ChildItem -LiteralPath $testRoot -Filter 'AocFramework-current.tsv').Count -ne 1){throw 'Unexpected rolling output'}
foreach($invalid in @([double]::NaN,[double]103)){
 [RollingFixtureClock]::Day=104
 $capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,$clock))|Out-Null
 [RollingFixtureClock]::Day=$invalid
 $write.Invoke($null,@('INVALID_CLOCK',[long]0,[long]0,'fixture','bad',[double]0,[double]0,''))|Out-Null
 if($capture.GetProperty('Active',$flags).GetValue($null,$null)){throw 'Invalid observation clock did not fail closed'}
}
'PASS: repeated 30-day rotation, paused wall time, reload append/anchor continuity and rollback refusal without data loss. Temporary fixtures; in-game acceptance remains required.'
$capture.GetMethod('BeginSession',$flags).Invoke($null,@([string]$testRoot,[Func[double]]{104}))|Out-Null
$request=$capture.GetProperty('CloseRequestPath',$flags).GetValue($null,$null)
New-Item -ItemType File -Path $request|Out-Null
$capture.GetField('_depth',$flags).SetValue($null,1)
$close=$capture.GetMethod('TryRequestedClose',$flags)
$callback=[Action]{ $write.Invoke($null,@('PROCUREMENT_LEDGER',[long]0,[long]0,'fixture','current_terminal',[double]0,[double]0,'synthetic'))|Out-Null }
if($close.Invoke($null,@($callback))){throw 'Diagnostic close interrupted a native scope'}
$capture.GetField('_depth',$flags).SetValue($null,0)
if(-not $close.Invoke($null,@($callback)) -or $capture.GetProperty('Active',$flags).GetValue($null,$null) -or (Test-Path -LiteralPath $request)){throw 'Safe diagnostic close did not acknowledge request'}
$latest=Get-ChildItem -LiteralPath $testRoot -Filter 'AocSupply-*.tsv'|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 1
$rows=@(Import-Csv -LiteralPath $latest.FullName -Delimiter "`t")
if($rows.Count -ne 3 -or $rows[0].metric -ne 'current_terminal' -or $rows[1].kind -ne 'DIAGNOSTIC_COST' -or $rows[2].kind -ne 'SESSION_END'){throw 'Terminal snapshot and cost receipt must precede diagnostic closure'}
'PASS: requested close defers open native scopes, captures terminal evidence and acknowledges only after closure; no game control.'
