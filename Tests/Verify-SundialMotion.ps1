$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
[xml]$brush=Get-Content -Raw (Join-Path $root 'GUI/Brushes/AocMapBar.xml')
[xml]$layout=Get-Content -Raw (Join-Path $root 'GUI/Prefabs/Map/MapBar.xml')
$dial=$layout.SelectSingleNode('//*[@Id="VanillaDayNightDisc"]')
if($dial.DayTime -ne '@Time'){throw 'Sundial must use actual VM time.'}
$layers=$brush.SelectNodes('/Brushes/Brush[@Name="AocMapTimeImageLarge"]/Layers/BrushLayer')
$ratio=[double]$dial.SuggestedHeight/50
if([Math]::Abs([double]$dial.SuggestedWidth-78*$ratio) -gt 0.00001){throw 'Native widget proportions changed.'}
foreach($layer in $layers){
    if([Math]::Abs([double]$layer.OverridenWidth-176*$ratio) -gt 0.00001 -or
       [Math]::Abs([double]$layer.OverridenHeight-[double]$dial.SuggestedHeight) -gt 0.00001){throw 'Native sweep width or full widget-height coverage changed.'}
    $clipBottom=[double]$dial.SuggestedHeight/2+[double]$dial.CircularClipRadius
    if([double]$layer.OverridenHeight -lt $clipBottom){throw 'Day/night strip exposes the bottom of the aperture.'}
    foreach($hours in @(1,6,12,24)){
        $nativeTravel=176*$hours/24/50
        $customTravel=[double]$layer.OverridenWidth*$hours/24/[double]$layer.OverridenHeight
        if([Math]::Abs($nativeTravel-$customTravel) -gt 0.00001){throw 'Sundial sweep differs from vanilla.'}
    }
}
if([Math]::Abs([double]$layers[1].XOffset-15*$ratio) -gt 0.00001){throw 'Native strip offset changed.'}
Write-Output 'PASS: proportional native sprite and widget geometry; sweep at 1/6/12/24 hours; full aperture coverage; live time binding. Not a live campaign animation test.'
