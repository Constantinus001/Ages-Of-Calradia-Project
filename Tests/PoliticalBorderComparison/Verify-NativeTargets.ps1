param([string]$GameRoot='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Run in Windows PowerShell for Harmony 2.4.2 .NET Framework.'}
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameBin=Join-Path $GameRoot 'bin\Win64_Shipping_Client'
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]$eventArgs.Name).Name+'.dll'
    foreach($folder in @($gameBin,(Join-Path $GameRoot 'Modules\SandBox\bin\Win64_Shipping_Client'),(Join-Path $GameRoot 'Modules\SandBoxCore\bin\Win64_Shipping_Client'))){
        $path=Join-Path $folder $name
        if(Test-Path -LiteralPath $path){return [Reflection.Assembly]::LoadFrom($path)}
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$type=$null
try {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'Modules\AOC CORE\bin\Win64_Shipping_Client\AgesOfCalradia.PoliticalFillSeamFix.dll'))
    $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'Builds\PoliticalBorderComparison\bin\Release\AgesOfCalradia.PoliticalBorderComparison.dll'))
    $type=$assembly.GetType('AgesOfCalradia.PoliticalBorderComparison.BorderComparison',$true)
    $flags=[Reflection.BindingFlags]'Static,NonPublic'
    $type.GetMethod('Initialize',$flags).Invoke($null,@())
    if(-not $type.GetField('_enabled',$flags).GetValue($null)){throw 'Border comparison initialization failed.'}
    $targets=@([HarmonyLib.Harmony]::GetAllPatchedMethods() | Where-Object {[HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'aoc.tests.border-comparison.v1'})
    if($targets.Count -ne 2){throw 'Expected two narrowly scoped observer targets.'}
    if(@($targets | Where-Object {$_.Name -notin @('Commit','ApplyFrontierZoomPresentation')}).Count){throw 'Unexpected patch target.'}
    $type.GetMethod('Tick',$flags).Invoke($null,@())
    if($type.GetField('_thread',$flags).GetValue($null) -ne [Threading.Thread]::CurrentThread.ManagedThreadId){throw 'Application thread binding failed.'}
    Write-Output 'PASS: two pinned targets bind; no fill geometry modification or global engine patch; application thread binding succeeds.'
} finally {
    if($type){$type.GetMethod('Stop',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}