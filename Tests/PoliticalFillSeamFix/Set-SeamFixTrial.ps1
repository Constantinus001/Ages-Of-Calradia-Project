param(
    [ValidateSet('Enable','Disable')][string]$Mode='Enable',
    [string]$InstalledRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE'
)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dllName='AgesOfCalradia.PoliticalFillSeamFix.dll'
$entry='AgesOfCalradia.PoliticalFillSeamFix.PoliticalFillSeamFixSubModule'
$built=Join-Path $root ('Builds/PoliticalFillSeamFix/bin/Release/'+$dllName)
$manifest=Join-Path $InstalledRoot 'SubModule.xml'
if(@(Get-Process 'Bannerlord*','TaleWorlds*' -ErrorAction SilentlyContinue).Count) {
    throw 'Close Bannerlord and its launcher before changing the fill seam trial registration.'
}
& (Join-Path $root 'Tests/Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $InstalledRoot
$before=[IO.File]::ReadAllText($manifest)
[xml]$beforeXml=$before
$existing=@($beforeXml.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq $dllName})
if($existing.Count -gt 1){throw 'Duplicate seam correction registrations require inspection.'}
if($Mode -eq 'Enable' -and $existing.Count){throw 'Already enabled; disable before replacing a trial DLL.'}
if($Mode -eq 'Disable' -and !$existing.Count){Write-Output 'Fill seam trial already disabled.'; return}
if($Mode -eq 'Enable' -and !(Test-Path -LiteralPath $built -PathType Leaf)){throw 'Build and verify the seam correction before registration.'}
$snapshot=Join-Path $root ('output/diagnostics/fill-seam-trial-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $snapshot | Out-Null
Copy-Item -LiteralPath $manifest -Destination (Join-Path $snapshot 'SubModule.before.xml')
$target=Join-Path $InstalledRoot ('bin/Win64_Shipping_Client/'+$dllName)
if($Mode -eq 'Enable') {
    if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination (Join-Path $snapshot $dllName)}
    $block=@"
    <SubModule>
      <Name value="AOC CORE: Political Fill Seam Correction Trial" />
      <DLLName value="$dllName" />
      <SubModuleClassType value="$entry" />
      <Tags>
        <Tag key="DedicatedServerType" value="none" />
        <Tag key="IsNoRenderModeElement" value="false" />
      </Tags>
    </SubModule>
"@
    $updated=$before.Replace('  </SubModules>',($block+"`r`n  </SubModules>"))
} else {
    $pattern='(?ms)^    <SubModule>\r?\n(?:(?!    </SubModule>).)*?<DLLName value="AgesOfCalradia\.PoliticalFillSeamFix\.dll" />(?:(?!    </SubModule>).)*?    </SubModule>\r?\n'
    $updated=[regex]::Replace($before,$pattern,'')
}
if($updated -ceq $before){throw 'Expected seam-trial manifest change not found.'}
[xml]$candidate=$updated
foreach($doc in @($candidate,$beforeXml)) {
    foreach($node in @($doc.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq $dllName})) {
        [void]$node.ParentNode.RemoveChild($node)
    }
}
if($candidate.OuterXml -cne $beforeXml.OuterXml){throw 'Unexpected unrelated manifest change.'}
if($Mode -eq 'Enable') {
    Copy-Item -LiteralPath $built -Destination $target
    if((Get-FileHash -LiteralPath $built).Hash -ne (Get-FileHash -LiteralPath $target).Hash){throw 'Trial DLL copy hash mismatch.'}
}
[IO.File]::WriteAllText($manifest,$updated,[Text.UTF8Encoding]::new($false))
if([IO.File]::ReadAllText($manifest) -cne $updated){throw 'Trial manifest readback mismatch.'}
& (Join-Path $root 'Tests/Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $InstalledRoot
"Mode=$Mode; time=$([DateTimeOffset]::Now.ToString('O')); protected artifacts unchanged; only fill seam trial registration modified." | Set-Content (Join-Path $snapshot 'receipt.log')
Write-Output "PASS: fill seam trial $Mode; backup=$snapshot"
