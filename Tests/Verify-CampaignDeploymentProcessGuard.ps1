$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CampaignDeploymentProcessGuard.ps1')
$checked=0
foreach($name in @('Bannerlord','Bannerlord.Native','Bannerlord.BLSE.Launcher',
 'Bannerlord.BLSE.LauncherEx','Launcher.Native','TaleWorlds.MountAndBlade.Launcher',
 'bannerlord')){
 $reader={ [pscustomobject]@{ ProcessName=$name } }.GetNewClosure()
 $rejected=$false
 try { Assert-CampaignDeploymentIdle -ReadProcesses $reader }
 catch { if($_.Exception.Message -notlike 'Close Bannerlord*'){throw};$rejected=$true }
 if(-not $rejected){throw "Failed to reject $name"};$checked++
}
Assert-CampaignDeploymentIdle -ReadProcesses { @() };$checked++
Assert-CampaignDeploymentIdle -ReadProcesses {
 @('Steam','powershell','BannerlordHelper') | ForEach-Object { [pscustomobject]@{ProcessName=$_} }
};$checked++
$failedClosed=$false
try { Assert-CampaignDeploymentIdle -ReadProcesses { throw 'enumeration unavailable' } }
catch { if($_.Exception.Message -ne 'enumeration unavailable'){throw};$failedClosed=$true }
if(-not $failedClosed){throw 'Enumeration failure was swallowed'};$checked++
"PASS: $checked deployment process guard assertions; no processes started or stopped."
