param(
 [string]$BannerlordDir='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
 [string]$LogDirectory=(Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics')
)
$ErrorActionPreference='Stop'
$moduleRoot=Split-Path -Parent $PSScriptRoot
$candidate=Join-Path $moduleRoot 'bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'
$installed=Join-Path $BannerlordDir 'Modules\AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'
if((Get-FileHash -LiteralPath $candidate).Hash -ne (Get-FileHash -LiteralPath $installed).Hash){throw 'Deploy the verified diagnostics DLL first. This script does not deploy or restart the game.'}
if(Get-Process | Where-Object {$_.ProcessName -match 'Bannerlord|TaleWorlds.*Launcher'}){throw 'Close the game/launcher yourself before arming this newly built capture, so the installed DLL will be loaded. No process was stopped.'}
$drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($LogDirectory))
if($drive.AvailableFreeSpace -lt 6442450944){throw 'Broad capture requires 6 GiB free (5 GiB log cap plus reserve). Nothing armed.'}
foreach($check in @('Verify-WoolOutParameter.ps1','Verify-SupplyCash.ps1')){
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $check)
 if($LASTEXITCODE -ne 0){throw "Preflight failed: $check. Nothing armed."}
}
foreach($name in @('AocSupplyCapture.enabled','AocFrameworkDiagnostics.enabled','AocEconomyShortCapture.enabled','AocEconomyTransactions.enabled')){
 if(Test-Path -LiteralPath (Join-Path $LogDirectory $name)){throw "Existing capture request $name; resolve its intended run before arming another capture."}
}
New-Item -ItemType Directory -Force -Path $LogDirectory|Out-Null
$marker=Join-Path $LogDirectory 'AocSupplyCapture.enabled'
$stream=[IO.File]::Open($marker,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{
 $bytes=[Text.Encoding]::UTF8.GetBytes('Broad v7 observer: next campaign load; rolling 30 campaign days; overwrites only AocFramework-current.tsv each cycle. Safety stop at 5 GiB per cycle; pausing does not expire the capture. Canonical wallets, nested transfer receipts and batch diagnostics. No speed, save or quit changes.')
 $stream.Write($bytes,0,$bytes.Length)
}finally{$stream.Dispose()}
"Armed for the next campaign load: $marker"
'Use the agreed AOC save. No save was selected, loaded or modified. Capture consumes this marker once.'
