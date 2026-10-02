param(
    [string]$Root=(Split-Path -Parent $PSScriptRoot),
    [string]$InstalledModuleRoot='C:/Program Files/Steam/steamapps/common/Mount & Blade II Bannerlord/Modules/AOC CORE',
    [string]$PreviousManifest,
    [ValidateSet('August28Original','August29V063')][string]$BorderBaseline='August28Original',
    [switch]$SkipInstalled
)
$ErrorActionPreference='Stop'
& (Join-Path $Root 'Tests/Verify-ProtectedPoliticalBaseline.ps1') -SkipInstalled:$SkipInstalled
$optimizer='AgesOfCalradia.PoliticalBorderOptimizer.dll'
$expectedHash='FC2B81B43F48E2028BAA79A5C5C67C8FBA324153CCFD77FE1DCBA3EE2497ED42'
$disabled=@('AgesOfCalradia.PoliticalBorderEditor.dll')
$paths=@((Join-Path $Root 'SubModule.xml'))
if(-not $SkipInstalled){$paths+=Join-Path $InstalledModuleRoot 'SubModule.xml'}
foreach($path in $paths){
    [xml]$manifest=Get-Content -Raw -LiteralPath $path
    $entries=@($manifest.Module.SubModules.SubModule)
    if(@($entries | Where-Object {$_.DLLName.value -in $disabled}).Count){throw "Border editor must remain disabled: $path"}
    $borderEntries=@($entries | Where-Object {$_.DLLName.value -eq $optimizer})
    if($BorderBaseline -eq 'August28Original'){
        if($borderEntries.Count -ne 0){throw "August 28 borders must use the protected renderer without an optimizer: $path"}
    }else{
        if($borderEntries.Count -ne 1 -or $borderEntries[0].SubModuleClassType.value -ne 'AgesOfCalradia.PoliticalBorderOptimizer.PoliticalBorderOptimizerSubModule'){
            throw "Historical optimizer entry point missing or duplicated: $path"
        }
        $binary=Join-Path (Split-Path -Parent $path) ('bin/Win64_Shipping_Client/'+$optimizer)
        if((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -ne $expectedHash){throw "Not the August 29 12:15 PM v0.6.3 binary: $binary"}
    }
    foreach($required in @('AgesOfCalradia.MySubModule','AgesOfCalradia.WorldEventsShellRepair.WorldEventsShellRepairSubModule','AgesOfCalradia.Approved560CalendarFixes.Approved560CalendarFixesSubModule','AgesOfCalradia.CampaignLabelVisibility.CampaignLabelVisibilitySubModule')){
        if(@($entries | Where-Object {$_.SubModuleClassType.value -eq $required}).Count -ne 1){throw "Required retained submodule missing or duplicated: $required"}
    }
    if($PreviousManifest){
        [xml]$previous=Get-Content -Raw -LiteralPath $PreviousManifest
        foreach($entry in @($previous.Module.SubModules.SubModule)){
            if($entry.DLLName.value -eq $optimizer){[void]$entry.ParentNode.RemoveChild($entry)}
        }
        foreach($entry in @($manifest.Module.SubModules.SubModule)){
            if($entry.DLLName.value -eq $optimizer){[void]$entry.ParentNode.RemoveChild($entry)}
        }
        if($previous.OuterXml -cne $manifest.OuterXml){throw "Restoration changed more than the optimizer registration: $path"}
    }
}
Write-Output "PASS: $BorderBaseline border configuration; editor disabled; retained modules and protected artifacts verified."
