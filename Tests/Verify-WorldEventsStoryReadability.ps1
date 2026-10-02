$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$gameBin='C:/Program Files/Steam/steamapps/common/Mount & Blade II Bannerlord/bin/Win64_Shipping_Client'
[void][Reflection.Assembly]::LoadFrom('C:/Users/fpicc/.nuget/packages/lib.harmony/2.4.2/lib/net472/0Harmony.dll')
foreach($name in @('TaleWorlds.Library','TaleWorlds.Core','TaleWorlds.ObjectSystem','TaleWorlds.Localization','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade','TaleWorlds.GauntletUI.PrefabSystem')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'tmp/world-events-shell-repair/bin/Win64_Shipping_Client/AgesOfCalradia.WorldEventsShellRepair.dll'))
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$repair=$assembly.GetType('AgesOfCalradia.WorldEventsShellRepair.WorldEventsChronicleTextRepair',$true)
$format=$repair.GetMethod('RepairPlaceholder',$flags)
$fixture="April 1st 1084 - Clan Playerland stands at tier 0.`nApril 1st 1084 - Clan Playerland is independent."
$expected=$fixture.Replace('Clan Playerland','Clan Lastrys')
if($format.Invoke($null,@($fixture,'Lastrys')) -cne $expected){throw 'Recorded placeholder repair failed'}
foreach($name in @('Playerland','', ' ')) {
    if($format.Invoke($null,@($fixture,$name)) -cne $fixture){throw 'Unknown or actual Playerland clan was rewritten'}
}
$unrelated='Clan Playerlandia is independent. Clan OldName stands at tier 2. Playerland was visited.'
if($format.Invoke($null,@($unrelated,'Lastrys')) -cne $unrelated){throw 'Unrelated historical names changed'}
[xml]$xml=Get-Content -Raw (Join-Path $root 'GUI/Prefabs/WorldCalendar/WorldCalendar.xml')
$frame=$xml.SelectSingleNode('//*[@Id="WorldEventsFrame"]')
$copy=$frame.CloneNode($true)
$original=$frame.OuterXml
$prefab=$assembly.GetType('AgesOfCalradia.WorldEventsShellRepair.WorldEventsShellPrefabPatch',$true)
$prefab.GetMethod('ImproveStoryReadability',$flags).Invoke($null,@($copy)) | Out-Null
foreach($id in @('CharacterStoryBody','CharacterMilestoneBody','PersonalChronicleSubtitle')) {
    $node=$copy.SelectSingleNode(".//*[@Id='$id']")
    $expectedFont=if($id -eq 'PersonalChronicleSubtitle'){'16'}else{'19'}
    if($node.GetAttribute('Brush.FontSize') -ne $expectedFont){throw "Font change failed: $id"}
    $node.SetAttribute('Brush.FontSize',$frame.SelectSingleNode(".//*[@Id='$id']").GetAttribute('Brush.FontSize'))
}
if($copy.OuterXml -cne $original -or $frame.OuterXml -cne $original){throw 'Readability changed layout, bindings, or original XML'}
$allTabs=$frame.CloneNode($true)
$transform=$assembly.GetType('AgesOfCalradia.WorldEventsShellRepair.WorldEventsReadability',$true)
$transform.GetMethod('Apply',$flags).Invoke($null,@($allTabs)) | Out-Null
$beforeText=@($frame.SelectNodes('.//TextWidget') | ForEach-Object { $_.GetAttribute('Text') } | Sort-Object)
$afterText=@($allTabs.SelectNodes('.//TextWidget') | ForEach-Object { $_.GetAttribute('Text') } | Sort-Object)
if(($beforeText -join '|') -cne ($afterText -join '|')){throw 'All-tab readability lost text or bindings'}
$beforeCommands=@($frame.SelectNodes('.//@*') | Where-Object Name -like 'Command.*' | ForEach-Object { $_.Value } | Sort-Object)
$afterCommands=@($allTabs.SelectNodes('.//@*') | Where-Object Name -like 'Command.*' | ForEach-Object { $_.Value } | Sort-Object)
if(($beforeCommands -join '|') -cne ($afterCommands -join '|')){throw 'All-tab readability changed commands'}
foreach($text in $allTabs.SelectNodes('.//TextWidget')) {
    if($text.GetAttribute('Text') -in @('◆','@Glyph','P','C','A')){continue}
    $size=$text.GetAttribute('Brush.FontSize')
    if($size -match '^\d+$' -and [int]$size -lt 16){throw 'Unaddressed small UI font'}
}
$scrolls=$allTabs.SelectNodes('.//ScrollablePanel[starts-with(@Id,"WorldEventsReadableDetail")]')
if($scrolls.Count -lt 3){throw 'Long detail fields were not made scrollable'}
foreach($scroll in $scrolls){
    $clip=$scroll.SelectSingleNode('Children/Widget')
    $body=$clip.SelectSingleNode('Children/TextWidget')
    if($clip.ClipContents -ne 'true' -or $body.HeightSizePolicy -ne 'CoverChildren' -or $scroll.InnerPanel -ne ($clip.Id+'\'+$body.Id)) {throw 'Scrollable detail path or content sizing is invalid'}
}
if($frame.OuterXml -cne $original){throw 'Original frame was modified'}
Write-Output ('PASS: all-tab font floor, text/commands preserved, '+$scrolls.Count+' scrollable detail fields; original frame unchanged.')
$renderer=[Reflection.Assembly]::LoadFrom((Join-Path $root 'bin/Win64_Shipping_Client/AgesOfCalradia.dll'))
$harmony=[HarmonyLib.Harmony]::new('AOC.Tests.StoryReadability')
try {
    $repair.GetMethod('Install',$flags).Invoke($null,@($harmony,$renderer)) | Out-Null
    $target=[HarmonyLib.AccessTools]::Method($renderer.GetType('TwelveMonthCalendar.CalendarWorldLedgerBehavior'),'GetCharacterMilestoneStory')
    $info=[HarmonyLib.Harmony]::GetPatchInfo($target)
    if(@($info.Postfixes | Where-Object owner -eq 'AOC.Tests.StoryReadability').Count -ne 1){throw 'Approved milestone target not patched'}
} finally { $harmony.UnpatchAll('AOC.Tests.StoryReadability') }
Write-Output 'PASS: exact placeholder labels, unrelated history preserved, three font-only changes, original XML untouched, approved target patched.'
