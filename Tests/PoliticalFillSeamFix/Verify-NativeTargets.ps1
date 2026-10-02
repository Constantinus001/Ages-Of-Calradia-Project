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
try{
    [void][Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\Win64_Shipping_Client\AgesOfCalradia.dll'))
    $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'Builds\PoliticalFillSeamFix\bin\Release\AgesOfCalradia.PoliticalFillSeamFix.dll'))
    $type=$assembly.GetType('AgesOfCalradia.PoliticalFillSeamFix.NativeFillSeamFix',$true)
    $flags=[Reflection.BindingFlags]'Static,NonPublic'
    $type.GetMethod('Initialize',$flags).Invoke($null,@())
    if(-not $type.GetField('_enabled',$flags).GetValue($null)){throw 'Seam adapter initialization failed; inspect build output Logs.'}
    $targets=@([HarmonyLib.Harmony]::GetAllPatchedMethods() | Where-Object {[HarmonyLib.Harmony]::GetPatchInfo($_).Owners -contains 'aoc.political-fill-seam-fix.v1'})
    if($targets.Count -ne 5){throw "Expected five exact mod targets, got $($targets.Count)."}
    if(@($targets | Where-Object {$_.DeclaringType.FullName -eq 'TaleWorlds.Engine.Mesh'}).Count){throw 'No global native Mesh patch is permitted.'}
    $meshCall=[TaleWorlds.Engine.Mesh].GetMethod('AddTriangle')
    $code=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
    $code.Add([HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Callvirt,$meshCall))
    $code.Add([HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Callvirt,$meshCall))
    $transpiler=$type.GetMethod('CaptureFirstFace',$flags)
    $transpileArguments=New-Object object[] 1
    $transpileArguments[0]=$code
    $result=@($transpiler.Invoke($null,$transpileArguments))
    if($result.Count -ne 2 -or $result[0].operand.Name -ne 'SubmitAndCapture' -or $result[1].operand -ne $meshCall){throw 'Relay changed more than the first native face submission.'}
    $bad=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
    $bad.Add([HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Callvirt,$meshCall))
    $rejected=$false
    $transpileArguments[0]=$bad
    try{$null=$transpiler.Invoke($null,$transpileArguments)}catch{if($_.Exception.InnerException -is [InvalidOperationException]){$rejected=$true}else{throw}}
    if(-not $rejected){throw 'Changed IL pattern was not rejected.'}
    $type.GetMethod('Tick',$flags).Invoke($null,@())
    if($type.GetField('_applicationThread',$flags).GetValue($null) -ne [Threading.Thread]::CurrentThread.ManagedThreadId){throw 'Application tick did not establish native thread.'}
    Write-Output 'PASS: five approved mod targets Harmony-bound; exact two-call relay validated; changed IL rejected; no global Mesh patch or native invocation; application tick affinity validated.'
}finally{
    if($type){$type.GetMethod('Stop',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
