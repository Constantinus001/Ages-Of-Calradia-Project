$ErrorActionPreference='Stop'
# Loads the compiled UI-only repair and exercises its other protected contracts.
. (Join-Path $PSScriptRoot 'Verify-WorldEventsStoryReadability.ps1')
[xml]$map=Get-Content -Raw (Join-Path $root 'output/mapbar-scaled/MapBar.xml')
$underlay=$map.SelectSingleNode('//*[@Id="ExpandedMapBarUnderlay"]')
$center=$map.SelectSingleNode('//*[@Id="CenterPanel"]')
$button=$map.SelectSingleNode('//*[@Id="WorldCalendarButton"]')
if($center.DoNotAcceptEvents -ne 'true' -or $center.DoNotPassEventsToChildren -eq 'true') {throw 'Center background must pass hits through while preserving child input'}
if($button.GetAttribute('Command.Click') -ne 'ExecuteToggleWorldCalendar' -or $button.DoNotPassEventsToChildren -ne 'true'){throw 'W must own the opening command'}
$originalToggle=$xml.SelectSingleNode('//*[@Id="WorldCalendarOpenOverlayToggle"]')
$before=$originalToggle.OuterXml
$arguments=[object[]]@($originalToggle)
$prefab.GetMethod('BeforeWidgetTemplateLoad',$flags).Invoke($null,$arguments) | Out-Null
$toggle=$arguments[0]
if($originalToggle.OuterXml -cne $before){throw 'Protected toggle node was modified'}
$width=[double]$button.SuggestedWidth
$height=[double]$button.SuggestedHeight
$centerX=[double]$underlay.SuggestedWidth/2+[double]$button.PositionXOffset-$width/2+[double]$underlay.PositionXOffset
$bottom=[double]$underlay.SuggestedHeight-[double]$button.PositionYOffset-$height-[double]$underlay.PositionYOffset
foreach($pair in @(@($width,[double]$toggle.SuggestedWidth),@($height,[double]$toggle.SuggestedHeight),@($centerX,[double]$toggle.PositionXOffset),@($bottom,[double]$toggle.MarginBottom))) {
    if([Math]::Abs($pair[0]-$pair[1]) -gt 0.001){throw 'Open-screen close hitbox does not match visible scaled W'}
}
if($toggle.GetAttribute('Command.Click') -ne 'ExecuteClose' -or $toggle.AlphaFactor -ne $originalToggle.AlphaFactor){throw 'Close command or invisible rendering changed'}
# Corners, center, and edge midpoints must all be inside the closing rectangle.
foreach($x in @(($centerX-$width/2+0.01),$centerX,($centerX+$width/2-0.01))) {
    foreach($y in @((-$bottom-$height+0.01),(-$bottom-$height/2),(-$bottom-0.01))) {
        if($x -lt [double]$toggle.PositionXOffset-[double]$toggle.SuggestedWidth/2 -or $x -gt [double]$toggle.PositionXOffset+[double]$toggle.SuggestedWidth/2 -or $y -lt -[double]$toggle.MarginBottom-[double]$toggle.SuggestedHeight -or $y -gt -[double]$toggle.MarginBottom){throw 'W edge sample misses close target'}
    }
}
Write-Output 'PASS: W input ownership, native child input retained, matching open/close rectangles, nine edge/center samples, original protected node unchanged.'
