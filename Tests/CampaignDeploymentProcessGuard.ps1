# Deployment boundary only: never stop processes or change launcher settings.
function Assert-CampaignDeploymentIdle {
 param([scriptblock]$ReadProcesses = { Get-Process -ErrorAction Stop })
 # Enumerate rather than querying names with SilentlyContinue: an enumeration
 # failure is not evidence that the game is closed. Exact names avoid blocking Steam.
 $names=@('Bannerlord','Bannerlord.Native','Bannerlord.BLSE.Launcher',
  'Bannerlord.BLSE.LauncherEx','Launcher.Native','TaleWorlds.MountAndBlade.Launcher')
 $active=@(& $ReadProcesses | Where-Object { $_.ProcessName -in $names })
 if($active.Count){
  throw ('Close Bannerlord and its launchers before deployment: '+
   (($active | ForEach-Object { $_.ProcessName } | Sort-Object -Unique) -join ', '))
 }
}
