param(
    [ValidateSet('Enable','Disable')][string]$Mode='Enable',
    [string]$InstalledRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE'
)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dllName='AgesOfCalradia.CoastSurfaceFix.dll'
$built=Join-Path $root ('Builds/CoastSurfaceFix/bin/Release/'+$dllName)
$manifest=Join-Path $InstalledRoot 'SubModule.xml'
if(@(Get-Process 'Bannerlord*','TaleWorlds*' -ErrorAction SilentlyContinue).Count){throw 'Game or launcher is running; no files changed.'}
& (Join-Path $root 'Tests/Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $InstalledRoot
$fill=Join-Path $InstalledRoot 'bin/Win64_Shipping_Client/AgesOfCalradia.PoliticalFillSeamFix.dll'
$fillHash='0918C5DBB2AD59BFAB9F768EE781D9F86A6DDDDE342D6D76A2BFA52EB88F063E'
if((Get-FileHash -LiteralPath $fill).Hash -ne $fillHash){throw 'Accepted fill hash changed; no deployment.'}
$before=[IO.File]::ReadAllText($manifest)
[xml]$beforeXml=$before
$existing=@($beforeXml.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq $dllName})
if($existing.Count -gt 1){throw 'Duplicate coast registrations.'}
if($Mode -eq 'Enable' -and $existing.Count){throw 'Already enabled; disable before replacing its DLL.'}
if($Mode -eq 'Disable' -and !$existing.Count){Write-Output 'Already disabled.';return}
if($Mode -eq 'Enable'){
    if(!(Test-Path -LiteralPath $built -PathType Leaf)){throw 'Release build missing.'}
    if(@($beforeXml.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq 'AgesOfCalradia.PoliticalFillSeamFix.dll'}).Count -ne 1){throw 'Accepted fill must be registered first.'}
    if(@($beforeXml.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq 'AgesOfCalradia.PoliticalBorderComparison.dll'}).Count){throw 'Rejected border comparison must remain disabled.'}
    $block=@"
    <SubModule>
      <Name value="AOC CORE: Coast Surface Correction" />
      <DLLName value="$dllName" />
      <SubModuleClassType value="AgesOfCalradia.CoastSurfaceFix.CoastSurfaceSubModule" />
      <Tags>
        <Tag key="DedicatedServerType" value="none" />
        <Tag key="IsNoRenderModeElement" value="false" />
      </Tags>
    </SubModule>
"@
    $updated=$before.Replace('  </SubModules>',($block+"`r`n  </SubModules>"))
} else {
    $pattern='(?ms)^    <SubModule>\r?\n(?:(?!    </SubModule>).)*?<DLLName value="AgesOfCalradia\.CoastSurfaceFix\.dll" />(?:(?!    </SubModule>).)*?    </SubModule>\r?\n'
    $updated=[regex]::Replace($before,$pattern,'')
}
if($updated -ceq $before){throw 'Expected manifest change not found.'}
[xml]$candidate=$updated
$expectedCount=if($Mode -eq 'Enable'){1}else{0}
if(@($candidate.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq $dllName}).Count -ne $expectedCount){throw 'Candidate registration count is incorrect.'}
foreach($doc in @($candidate,$beforeXml)){
    foreach($node in @($doc.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq $dllName})){[void]$node.ParentNode.RemoveChild($node)}
}
if($candidate.OuterXml -cne $beforeXml.OuterXml){throw 'Unrelated manifest mutation refused.'}
$snapshot=Join-Path $root ('output/deployment-backups/coast-surface-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $snapshot | Out-Null
Copy-Item -LiteralPath $manifest -Destination (Join-Path $snapshot 'SubModule.before.xml')
$target=Join-Path $InstalledRoot ('bin/Win64_Shipping_Client/'+$dllName)
$hadDll=Test-Path -LiteralPath $target
if($hadDll){Copy-Item -LiteralPath $target -Destination (Join-Path $snapshot $dllName)}
try {
    if($Mode -eq 'Enable'){
        Copy-Item -LiteralPath $built -Destination $target
        if((Get-FileHash -LiteralPath $built).Hash -ne (Get-FileHash -LiteralPath $target).Hash){throw 'DLL readback hash mismatch.'}
    }
    [IO.File]::WriteAllText($manifest,$updated,[Text.UTF8Encoding]::new($false))
    if([IO.File]::ReadAllText($manifest) -cne $updated){throw 'Manifest readback mismatch.'}
    & (Join-Path $root 'Tests/Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $InstalledRoot
    if((Get-FileHash -LiteralPath $fill).Hash -ne $fillHash){throw 'Accepted fill changed.'}
} catch {
    Copy-Item -LiteralPath (Join-Path $snapshot 'SubModule.before.xml') -Destination $manifest
    if($Mode -eq 'Enable' -and $hadDll){Copy-Item -LiteralPath (Join-Path $snapshot $dllName) -Destination $target}
    # A newly copied but unregistered DLL is inert; retain it as failure evidence.
    throw
}
"mode=$Mode;time=$([DateTimeOffset]::Now.ToString('O'));dllHash=$((Get-FileHash -LiteralPath $target).Hash);protected Core/prefab and accepted fill unchanged;visual validation pending" | Set-Content (Join-Path $snapshot 'receipt.log')
Write-Output "PASS: coast surface correction $Mode; backup=$snapshot"
