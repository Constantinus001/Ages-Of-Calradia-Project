param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$OutputRoot = (Join-Path $env:TEMP 'aoc-succession-verification'),
    [string]$NativeAuditAssemblyPath,
    [string]$IldasmPath = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.7.2 Tools\x64\ildasm.exe'
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run with Windows PowerShell: Harmony 2.4.2 net472 requires the desktop runtime for this audit.' }
$module = Split-Path -Parent $PSScriptRoot
$gameBin = Join-Path $BannerlordDir 'bin\Win64_Shipping_Client'
$campaign = Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'
function Read-NativeMethod([string]$Item) {
    $result = (& $IldasmPath $campaign /text "/item=$Item") -join "`n"
    if ($LASTEXITCODE -ne 0 -or $result -notmatch 'end of method') { throw "Native method missing: $Item" }
    return $result
}
$add = Read-NativeMethod 'TaleWorlds.CampaignSystem.Kingdom::AddDecision'
$eventIndex = $add.IndexOf('CampaignEventReceiver::OnKingdomDecisionAdded')
$startIndex = $add.IndexOf('KingdomElection::StartElection')
$enqueueIndex = $add.LastIndexOf('::Add(!0)')
if ($eventIndex -lt 0 -or $startIndex -le $eventIndex -or $enqueueIndex -le $eventIndex) { throw 'Native decision event/start/enqueue order changed; re-audit interception.' }
$apply = Read-NativeMethod 'TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision::ApplyChosenOutcome'
if ($apply -notmatch 'ChangeRulingClanAction::Apply') { throw 'Native ruler outcome no longer uses the audited ruling-clan action.' }
$death = Read-NativeMethod 'TaleWorlds.CampaignSystem.Actions.KillCharacterAction::ApplyInternal'
if ($death -notmatch 'CampaignEventReceiver::OnHeroKilled' -or $death -notmatch 'Hero::OnDeath') { throw 'Native death dispatcher contract changed.' }
foreach ($action in @('ChangeKingdomAction::ApplyByJoinToKingdom', 'MakePeaceAction::Apply', 'DestroyKingdomAction::Apply')) {
    $method = Read-NativeMethod ('TaleWorlds.CampaignSystem.Actions.' + $action)
    if ($method -notmatch 'ApplyInternal') { throw "Native claimant settlement boundary changed: $action" }
}
function Build-Checked([string]$Project, [string]$Folder, [string]$Diagnostics = 'false') {
    $out = Join-Path $OutputRoot "$Folder\"
    $obj = Join-Path $OutputRoot "$Folder-obj\"
    & dotnet msbuild $Project /t:Rebuild /p:Configuration=Release "/p:EnableSuccessionDiagnostics=$Diagnostics" "/p:BannerlordDir=$BannerlordDir" "/p:OutputPath=$out" "/p:IntermediateOutputPath=$obj" /p:TreatWarningsAsErrors=true /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $Project" }
}
Build-Checked (Join-Path $module 'AgesOfCalradiaSuccession.csproj') 'module'
$productionAssembly = Join-Path $OutputRoot 'module\AgesOfCalradiaSuccession.dll'
$startup = (& $IldasmPath $productionAssembly /text '/item=AgesOfCalradiaSuccession.SuccessionCampaignBehavior::OnSessionLaunched') -join "`n"
if ($LASTEXITCODE -ne 0 -or $startup -notmatch 'end of method' -or $startup -match 'settlement debug menu registration') {
    throw 'Release startup must exclude destructive diagnostics menu registration.'
}
Build-Checked (Join-Path $module 'AgesOfCalradiaSuccession.csproj') 'diagnostics' 'true'
$diagnosticAssembly = Join-Path $OutputRoot 'diagnostics\AgesOfCalradiaSuccession.dll'
$startup = (& $IldasmPath $diagnosticAssembly /text '/item=AgesOfCalradiaSuccession.SuccessionCampaignBehavior::OnSessionLaunched') -join "`n"
if ($LASTEXITCODE -ne 0 -or $startup -notmatch 'settlement debug menu registration') {
    throw 'Explicit diagnostics build must retain disposable-campaign test menus.'
}
Build-Checked (Join-Path $PSScriptRoot 'SuccessionDispatchVerifier.csproj') 'dispatch'
& (Join-Path $OutputRoot 'dispatch\SuccessionDispatchVerifier.exe')
if ($LASTEXITCODE -ne 0) { throw 'Dispatch behavioral verification failed.' }
Build-Checked (Join-Path $PSScriptRoot 'SuccessionEngineVerifier.csproj') 'engine'
& (Join-Path $OutputRoot 'engine\SuccessionEngineVerifier.exe')
if ($LASTEXITCODE -ne 0) { throw 'Production succession behavior verification failed.' }
Build-Checked (Join-Path $PSScriptRoot 'SuccessionPersistenceVerifier.csproj') 'persistence'
& (Join-Path $OutputRoot 'persistence\SuccessionPersistenceVerifier.exe')
if ($LASTEXITCODE -ne 0) { throw 'Existing persistence verification failed.' }
# Install actual sidecar prefixes on installed game methods in this disposable
# process. No campaign is created and no native game action is invoked.
$harmonyPath = Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'
[void][Reflection.Assembly]::LoadFrom($harmonyPath)
foreach ($name in @('TaleWorlds.Library','TaleWorlds.Localization','TaleWorlds.ObjectSystem','TaleWorlds.Core','TaleWorlds.SaveSystem','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin "$name.dll"))
}
if ([string]::IsNullOrWhiteSpace($NativeAuditAssemblyPath)) {
    $NativeAuditAssemblyPath = Join-Path $OutputRoot 'module\AgesOfCalradiaSuccession.dll'
}
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $NativeAuditAssemblyPath).Path)
$type = $assembly.GetType('AgesOfCalradiaSuccession.SuccessionElectionPatches', $true)
$install = $type.GetMethod('Install', [Reflection.BindingFlags]'Static,NonPublic')
[void]$install.Invoke($null, @())
[void]$install.Invoke($null, @())
$owner = 'agesofcalradia.succession.ruler-election'
$targets = @(
    [TaleWorlds.CampaignSystem.Kingdom].GetMethod('AddDecision'),
    [TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision].GetMethod('ApplyChosenOutcome')
)
foreach ($target in $targets) {
    $patches = [HarmonyLib.Harmony]::GetPatchInfo($target)
    if (@($patches.Prefixes | Where-Object { $_.owner -eq $owner }).Count -ne 1) { throw "Patch registration failed or duplicated: $target" }
}
Write-Host 'Succession native audit passed: installed method order/signatures, actual Harmony installation, no duplicate own patches.'
Write-Host 'This does not replace in-game death, player-heir, regency and competing-mod tests.'
