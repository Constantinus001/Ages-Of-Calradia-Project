param([string]$DiagnosticsAssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-SupplyCapture.ps1') -DiagnosticsAssemblyPath $DiagnosticsAssemblyPath
$ready=$assembly.GetType('AgesOfCalradia.SoakDiagnostics.CaptureReadiness',$true)
$rolling=$capture.GetMethod('BeginRollingSession',$flags)
$path=$rolling.Invoke($null,@([string]$testRoot,[Func[double]]{42.5}))
$receiptPath=Join-Path $testRoot 'AocFramework-readiness.json'
$receipt=Get-Content -Raw -LiteralPath $receiptPath|ConvertFrom-Json
if($receipt.status -ne 'PREPARING'){throw 'Premature recording claim'}
$rejected=$false
try{$ready.GetMethod('MarkReady',$flags).Invoke($null,@())}catch{$rejected=$true}
if(!$rejected){throw 'Ready without hooks accepted'}
try{
 $observer.GetMethod('Install',$flags).Invoke($null,@())|Out-Null
 $ready.GetMethod('ValidateHooks',$flags).Invoke($null,@())|Out-Null
 $capture.GetMethod('Flush',$flags).Invoke($null,@())|Out-Null
 $ready.GetMethod('MarkReady',$flags).Invoke($null,@())|Out-Null
 $capture.GetMethod('PulseReadiness',$flags).Invoke($null,@())|Out-Null
 $receipt=Get-Content -Raw -LiteralPath $receiptPath|ConvertFrom-Json
 if($receipt.status -ne 'RECORDING' -or $receipt.requiredHooks -le 0 -or $receipt.pid -ne $PID -or !$receipt.session -or $receipt.bytes -le 0){throw 'Incomplete recording proof'}
 if($receipt.assemblies[0].mvid -eq ''){throw 'Missing loaded MVID'}
 # Removing an actual hook invalidates readiness; never carry a stale green state.
 $observer.GetMethod('Uninstall',$flags).Invoke($null,@())|Out-Null
 $ready.GetField('_lastPulse',$flags).SetValue($null,[long]0)
 $capture.GetField('_flushTicks',$flags).SetValue($null,[long]0)
 $capture.GetMethod('Tick',$flags).Invoke($null,@())|Out-Null
 $receipt=Get-Content -Raw -LiteralPath $receiptPath|ConvertFrom-Json
 if($receipt.status -ne 'BLOCKED' -or $receipt.reason -notmatch 'hook changed'){throw 'Removed hooks left capture recording'}
}finally{$observer.GetMethod('Uninstall',$flags).Invoke($null,@())|Out-Null}
# Separate fixture directory: optional acceptance mode must not alter rolling defaults.
$bounded=Join-Path $testRoot 'bounded';New-Item -ItemType Directory -Path $bounded|Out-Null
$readDays=$capture.GetMethod('ReadAcceptanceDays',$flags)
if($readDays.Invoke($null,@([string]$bounded)) -ne 0){throw 'Normal rolling mode changed'}
foreach($invalid in @('0','30','5.5','garbage')){
 [IO.File]::WriteAllText((Join-Path $bounded 'AocAcceptance.days'),$invalid)
 $rejected=$false;try{$readDays.Invoke($null,@([string]$bounded))|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Invalid acceptance bound accepted'}
}
[IO.File]::WriteAllText((Join-Path $bounded 'AocAcceptance.days'),'5')
$boundedPath=$rolling.Invoke($null,@([string]$bounded,[Func[double]]{50}))
$capture.GetField('_sessionStartDay',$flags).SetValue($null,[double]45)
$capture.GetMethod('Tick',$flags).Invoke($null,@())|Out-Null
$rows=@(Import-Csv -LiteralPath $boundedPath -Delimiter "`t")
if($rows[-1].metric -ne 'acceptance_window_complete_coverage_not_certified'){throw 'Bounded capture did not close itself'}
$hash=(Get-FileHash -LiteralPath $boundedPath).Hash
$rejected=$false;try{$rolling.Invoke($null,@([string]$bounded,[Func[double]]{50}))|Out-Null}catch{$rejected=$true}
if(!$rejected -or (Get-FileHash -LiteralPath $boundedPath).Hash -ne $hash){throw 'Acceptance reused/overwrote prior evidence'}
'PASS: startup/loaded identity, hook removal fail-closed, bounded diagnostic-only closure, default rolling mode, fresh-session preservation.'
