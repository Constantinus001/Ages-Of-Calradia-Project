param([string]$GameRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run with Windows PowerShell for Harmony 2.4.2.' }
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameBin=Join-Path $GameRoot 'bin\Win64_Shipping_Client'
$resolver=[ResolveEventHandler]{ param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]$eventArgs.Name).Name+'.dll'
    foreach($folder in @($gameBin,(Join-Path $GameRoot 'Modules\SandBox\bin\Win64_Shipping_Client'),(Join-Path $GameRoot 'Modules\SandBoxCore\bin\Win64_Shipping_Client'))) {
        $path=Join-Path $folder $name
        if(Test-Path -LiteralPath $path){return [Reflection.Assembly]::LoadFrom($path)}
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$patchType=$null
try {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'))
    $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'Builds\PoliticalFillAlphaFix\bin\Release\AgesOfCalradia.PoliticalFillAlphaFix.dll'))
    $patchType=$assembly.GetType('AgesOfCalradia.PoliticalFillAlphaFix.FillAlphaPatch',$true)
    $flags=[Reflection.BindingFlags]'Static,NonPublic'
    $patchType.GetMethod('Initialize',$flags).Invoke($null,@())
    if(-not $patchType.GetField('_enabled',$flags).GetValue($null)){throw 'Alpha correction setup failed; inspect its status log.'}
    $targets=@([HarmonyLib.Harmony]::GetAllPatchedMethods() | Where-Object { [HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'aoc.political-fill-applied-alpha.v1' })
    if($targets.Count -ne 1 -or $targets[0].Name -ne 'ApplyPoliticalEntityVisibility'){throw 'Expected exactly the approved fill visibility method.'}
    $patches=[HarmonyLib.Harmony]::GetPatchInfo($targets[0])
    foreach($patch in @($patches.Prefixes)+@($patches.Postfixes)) {
        if($patch.owner -eq 'aoc.political-fill-applied-alpha.v1' -and $patch.PatchMethod.ReturnType -ne [void]) {throw 'Alpha correction must preserve original execution and return.'}
    }
    # Exercise only the managed adapter, never the original or a game entity.
    # Hidden fill must retain the native force input without repeatedly forcing
    # a loop merely because no alpha-zero assignment was committed.
    $behavior=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($targets[0].DeclaringType)
    $before=$patchType.GetMethod('BeforeVisibility',$flags)
    $invocationArguments=[object[]]@($behavior,$false,$null)
    $before.Invoke($null,$invocationArguments)
    if($invocationArguments[1] -ne $false -or $null -ne $invocationArguments[2]) {throw 'Hidden fill incorrectly forces a publication or allocates pending state.'}
    $forcedArgs=[object[]]@($behavior,$true,$null)
    $before.Invoke($null,$forcedArgs)
    if($forcedArgs[1] -ne $true) {throw 'Hidden fill lost an explicit native force request.'}
    Write-Output 'PASS: managed adapter preserves hidden-fill force decisions without repeatedly forcing publication.'
    Write-Output 'PASS: applied-alpha correction binds exactly one approved target; state and run-original injection validated. No scene invoked.'
} finally {
    if($patchType){$patchType.GetMethod('Stop',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
