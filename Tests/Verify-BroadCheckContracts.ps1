$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'StrategicMapPixelChecks.cs') -ReferencedAssemblies System.Drawing
function Must-Reject([scriptblock]$Action, [string]$Name) {
 $rejected=$false
 try { & $Action | Out-Null } catch { $rejected=$true }
 if (-not $rejected) { throw "Corruption accepted: $Name" }
}
$basis=[Drawing.Bitmap]::new(2,2)
$index=[Drawing.Bitmap]::new(2,2)
$atlas=[Drawing.Bitmap]::new(4,4)
$mask=[Drawing.Bitmap]::new(2,2)
try {
 # Exhaustive ARGB equality includes alpha and offsets, not just visible RGB.
 $mask.SetPixel(1,1,[Drawing.Color]::FromArgb(75,10,20,30))
 $atlas.SetPixel(2,2,$mask.GetPixel(1,1))
 [StrategicMapPixelChecks]::Atlas($atlas,$mask,1,1,'fixture')
 $atlas.SetPixel(2,2,[Drawing.Color]::FromArgb(74,10,20,30))
 Must-Reject { [StrategicMapPixelChecks]::Atlas($atlas,$mask,1,1,'fixture') } 'one alpha bit'
 for($y=0;$y -lt 2;$y++){for($x=0;$x -lt 2;$x++){$index.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,1,0,0))}}
 [StrategicMapPixelChecks]::Index($basis,$index,$false)|Out-Null
 $index.SetPixel(0,0,[Drawing.Color]::FromArgb(255,255,0,0))
 Must-Reject { [StrategicMapPixelChecks]::Index($basis,$index,$false) } 'province id 255'
 if([StrategicMapPixelChecks]::Index($basis,$index,$true) -ne 1){throw 'Settlement split-border count changed'}
 $index.SetPixel(0,0,[Drawing.Color]::FromArgb(0,1,0,0))
 Must-Reject { [StrategicMapPixelChecks]::Index($basis,$index,$true) } 'transparent settlement interior'
 $index.SetPixel(0,0,[Drawing.Color]::FromArgb(255,0,0,0))
 Must-Reject { [StrategicMapPixelChecks]::Index($basis,$index,$false) } 'unassigned province'
 $index.SetPixel(0,0,[Drawing.Color]::FromArgb(255,1,0,0))
 $basis.SetPixel(1,1,[Drawing.Color]::Black)
 Must-Reject { [StrategicMapPixelChecks]::Index($basis,$index,$true) } 'opaque water overwrite'
 $coverage=[StrategicMapPixelChecks]::Coverage($basis,$mask)
 if($coverage[0] -ne 3 -or $coverage[1] -ne 0){throw 'Land alpha/coverage predicate changed'}
 $mask.SetPixel(0,0,[Drawing.Color]::Red)
 if([StrategicMapPixelChecks]::Coverage($basis,$mask)[1] -ne 1){throw 'Covered land was not counted'}
 $composer='<TextureWidget TextureProviderName="CalendarStrategicCampaignAtlasTextureProvider" />'
 [StrategicMapPixelChecks]::Composer($composer+'<TextureWidget TextureProviderName="WorldEventsTreasuryPageTextureProvider" />')
 Must-Reject { [StrategicMapPixelChecks]::Composer($composer+$composer) } 'duplicate composer'
 Must-Reject { [StrategicMapPixelChecks]::Composer('<Widget/>') } 'missing composer'
} finally { $basis.Dispose();$index.Dispose();$atlas.Dispose();$mask.Dispose() }

# Known protected artifact must never pass schema-6 expectations. This runs in
# a fresh process and does not load or mutate any user save/settings.
$math=Join-Path $PSScriptRoot 'Verify-CalendarMath.ps1'
$ErrorActionPreference='Continue'
$failure=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $math -Contract Development 2>&1
$ErrorActionPreference='Stop'
if($LASTEXITCODE -eq 0 -or ($failure -join ' ') -notmatch "Expected '6'; actual '5'"){throw 'Development contract did not retain its schema-6 requirement'}
$otherAssembly=Join-Path $PSScriptRoot '..\Modules\AgesOfCalradiaSoakDiagnostics\bin\Win64_Shipping_Client\AgesOfCalradia.SoakDiagnostics.dll'
$ErrorActionPreference='Continue'
$failure=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $math -Contract Protected560 -CalendarAssemblyPath $otherAssembly 2>&1
$ErrorActionPreference='Stop'
if($LASTEXITCODE -eq 0 -or ($failure -join ' ') -notmatch 'requires the exact approved binary'){throw 'Protected contract accepted an unrelated binary'}
'PASS: exhaustive pixel corruption checks, composer count isolation, and development schema mismatch rejection.'
