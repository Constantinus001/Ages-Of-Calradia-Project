param(
    [ValidateSet('Enable','Disable')][string]$Mode = 'Enable',
    [string]$InstalledRoot = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dllName = 'AgesOfCalradia.PoliticalRenderDiagnostics.dll'
$entryType = 'AgesOfCalradia.PoliticalRenderDiagnostics.NativeDiagnosticsSubModule'
$builtDll = Join-Path $PSScriptRoot ('bin/Release/' + $dllName)
$manifestPath = Join-Path $InstalledRoot 'SubModule.xml'
# A running process has already read the manifest. Do not copy a locked module,
# or imply that reloading a save activates assembly registration changes.
if (@(Get-Process 'Bannerlord*','TaleWorlds*' -ErrorAction SilentlyContinue).Count) {
    throw 'Close Bannerlord and its launcher before changing the diagnostic registration.'
}
& (Join-Path $root 'Tests/Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $InstalledRoot
$before = [IO.File]::ReadAllText($manifestPath)
[xml]$beforeXml = $before
$existing = @($beforeXml.Module.SubModules.SubModule | Where-Object { $_.DLLName.value -eq $dllName })
if ($existing.Count -gt 1) { throw 'Duplicate diagnostic entries require inspection.' }
if ($Mode -eq 'Enable' -and $existing.Count) { throw 'Diagnostics already registered; disable before replacing the diagnostic DLL.' }
if ($Mode -eq 'Disable' -and !$existing.Count) { Write-Output 'Diagnostics already disabled.'; return }
if ($Mode -eq 'Enable' -and !(Test-Path -LiteralPath $builtDll -PathType Leaf)) { throw 'Build and verify the Release diagnostic assembly first.' }
$snapshot = Join-Path $root ('output/diagnostics/render-capture-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $snapshot | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $snapshot 'SubModule.before.xml')
$targetDll = Join-Path $InstalledRoot ('bin/Win64_Shipping_Client/' + $dllName)
if ($Mode -eq 'Enable') {
    if (Test-Path -LiteralPath $targetDll) { Copy-Item -LiteralPath $targetDll -Destination (Join-Path $snapshot $dllName) }
    $block = @"
    <SubModule>
      <Name value="AOC CORE: Temporary Political Render Diagnostics" />
      <DLLName value="$dllName" />
      <SubModuleClassType value="$entryType" />
      <Tags>
        <Tag key="DedicatedServerType" value="none" />
        <Tag key="IsNoRenderModeElement" value="false" />
      </Tags>
    </SubModule>
"@
    $updated = $before.Replace('  </SubModules>', ($block + "`r`n  </SubModules>"))
} else {
    $pattern = '(?ms)^    <SubModule>\r?\n(?:(?!    </SubModule>).)*?<DLLName value="AgesOfCalradia\.PoliticalRenderDiagnostics\.dll" />(?:(?!    </SubModule>).)*?    </SubModule>\r?\n'
    $updated = [regex]::Replace($before, $pattern, '')
}
if ($updated -ceq $before) { throw 'No expected manifest change found.' }
[xml]$candidate = $updated
foreach ($doc in @($candidate, $beforeXml)) {
    foreach ($node in @($doc.Module.SubModules.SubModule | Where-Object { $_.DLLName.value -eq $dllName })) {
        [void]$node.ParentNode.RemoveChild($node)
    }
}
if ($candidate.OuterXml -cne $beforeXml.OuterXml) { throw 'Unexpected non-diagnostic manifest change.' }
if ($Mode -eq 'Enable') {
    Copy-Item -LiteralPath $builtDll -Destination $targetDll
    if ((Get-FileHash -LiteralPath $builtDll).Hash -ne (Get-FileHash -LiteralPath $targetDll).Hash) { throw 'Diagnostic DLL copy verification failed.' }
}
[IO.File]::WriteAllText($manifestPath, $updated, [Text.UTF8Encoding]::new($false))
if ([IO.File]::ReadAllText($manifestPath) -cne $updated) { throw 'Manifest readback mismatch.' }
& (Join-Path $root 'Tests/Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $InstalledRoot
"Mode=$Mode; capturedAt=$([DateTimeOffset]::Now.ToString('O')); protected artifacts unchanged; only temporary diagnostics registration changed; no renderer or save changes." | Set-Content (Join-Path $snapshot 'Receipt.log')
Write-Output "PASS: diagnostics $Mode; backup=$snapshot"
