param([Parameter(Mandatory=$true)][ValidateSet('A','B')][string]$Mode,
[string]$InstalledRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE')
$ErrorActionPreference='Stop'
$target=Join-Path $InstalledRoot 'bin/Win64_Shipping_Client/PoliticalBorderComparison.mode'
[IO.File]::WriteAllText($target,$Mode,[Text.UTF8Encoding]::new($false))
if([IO.File]::ReadAllText($target) -cne $Mode){throw 'Mode write verification failed.'}
Write-Output "Selected $Mode. Applied on next mode poll after border commit. B is a general depth override, not water-only."
