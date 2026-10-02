param(
    [string]$BannerlordDir = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$AssemblyPath = (Join-Path $PSScriptRoot 'bin\Release\AgesOfCalradiaInternalWarsTest.dll'),
    [string]$HarmonyPath = (Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.2\lib\net472\0Harmony.dll')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Standalone Windows PowerShell metadata audit. No PatchAll, prefix/postfix bodies,
# game methods, or campaign creation. TargetMethod helpers in this module only use AccessTools metadata.
# __state is a Harmony class-local transport slot, verified between participating patches below.
# This audit has no generic field injection exemption: unsupported special names fail closed.
[Reflection.Assembly]::LoadFrom($HarmonyPath) | Out-Null
$campaign = [Reflection.Assembly]::LoadFrom((Join-Path $BannerlordDir 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll'))
if ($campaign.ManifestModule.ModuleVersionId -ne [Guid]'886629fe-6e60-40d7-9a57-8d46017179d9') { throw 'Native CampaignSystem MVID changed.' }
$module = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($AssemblyPath))
foreach ($reference in $module.GetReferencedAssemblies()) {
    $dependency = Join-Path $BannerlordDir ('bin\Win64_Shipping_Client\' + $reference.Name + '.dll')
    if (Test-Path -LiteralPath $dependency) { [Reflection.Assembly]::LoadFrom($dependency) | Out-Null }
}
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
$script:assertions = 0
$patchCount = 0
function Assert-Binding([bool]$Condition, [string]$Message) {
    $script:assertions++
    if (-not $Condition) { throw $Message }
}
function Unwrap([Type]$Type) { if ($Type.IsByRef) { return $Type.GetElementType() }; return $Type }
foreach ($type in $module.GetTypes()) {
    $classAttributes = @($type.GetCustomAttributes([HarmonyLib.HarmonyPatch], $false))
    if ($classAttributes.Count -eq 0) { continue }
    $helper = $type.GetMethod('TargetMethod', $flags)
    if ($null -ne $helper) {
        Assert-Binding ($helper.IsStatic -and $helper.GetParameters().Count -eq 0) "$($type.Name) target helper signature changed."
        $original = $helper.Invoke($null, @())
    } else {
        $declaringType = $null; $methodName = $null; $arguments = $null
        foreach ($attribute in $classAttributes) {
            if ($null -ne $attribute.info.declaringType) { $declaringType = $attribute.info.declaringType }
            if ($null -ne $attribute.info.methodName) { $methodName = $attribute.info.methodName }
            if ($null -ne $attribute.info.argumentTypes) { $arguments = $attribute.info.argumentTypes }
        }
        Assert-Binding ($null -ne $declaringType -and $null -ne $methodName) "$($type.Name) has an unsupported target declaration."
        if ($null -ne $arguments) { $original = [HarmonyLib.AccessTools]::Method($declaringType, $methodName, $arguments) }
        else {
            $candidates = @($declaringType.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance,Static') | Where-Object { $_.Name -eq $methodName })
            Assert-Binding ($candidates.Count -eq 1) "$($type.Name): ambiguous or missing target $methodName."
            $original = $candidates[0]
        }
    }
    Assert-Binding ($null -ne $original) "$($type.Name): target not found."
    $stateType = $null
    foreach ($patch in $type.GetMethods($flags)) {
        $kind = @($patch.GetCustomAttributes($false) | Where-Object {
            $_.GetType().Name -in @('HarmonyPrefix','HarmonyPostfix','HarmonyTranspiler','HarmonyFinalizer')
        })
        if ($kind.Count -eq 0) { continue }
        $patchCount++
        Assert-Binding $patch.IsStatic "$($type.Name).$($patch.Name): patch must be static."
        foreach ($parameter in $patch.GetParameters()) {
            $context = "$($type.Name).$($patch.Name) parameter $($parameter.Name)"
            $actualType = Unwrap $parameter.ParameterType
            if ($kind[0].GetType().Name -eq 'HarmonyTranspiler') {
                $valid = $actualType -eq [Reflection.Emit.ILGenerator] -or [Reflection.MethodBase].IsAssignableFrom($actualType)
                if ($actualType.IsGenericType -and $actualType.GetGenericArguments().Count -eq 1) {
                    $valid = $valid -or ($actualType.GetGenericArguments()[0] -eq [HarmonyLib.CodeInstruction] -and [Collections.IEnumerable].IsAssignableFrom($actualType))
                }
                Assert-Binding $valid "$context unsupported transpiler injection."
                continue
            }
            switch ($parameter.Name) {
                '__instance' {
                    Assert-Binding (-not $original.IsStatic -and $actualType.IsAssignableFrom($original.DeclaringType)) "$context invalid instance injection."
                    continue
                }
                '__result' {
                    Assert-Binding ($original.ReturnType -ne [void] -and $actualType -eq $original.ReturnType) "$context result type mismatch."
                    continue
                }
                '__state' {
                    if ($null -eq $stateType) { $stateType = $actualType }
                    Assert-Binding ($actualType -eq $stateType) "$context class-local state type mismatch."
                    continue
                }
                '__exception' {
                    Assert-Binding ($actualType -eq [Exception]) "$context invalid exception injection."
                    continue
                }
            }
            if ($parameter.Name -in @('__instance','__result','__state','__exception')) { continue }
            $originalParameters = $original.GetParameters()
            if ($parameter.Name -match '^__(\d+)$') {
                $index = [int]$Matches[1]
                Assert-Binding ($index -lt $originalParameters.Count) "$context original argument index missing."
                $nativeParameter = $originalParameters[$index]
            } else {
                $matchesByName = @($originalParameters | Where-Object { $_.Name -eq $parameter.Name })
                Assert-Binding ($matchesByName.Count -eq 1) "$context original parameter name missing."
                $nativeParameter = $matchesByName[0]
            }
            # Harmony permits ref patch access to a native value argument and value reads of native ref arguments.
            # Compare underlying types; this module does not use object boxing or delegate coercion injections.
            Assert-Binding ($actualType -eq (Unwrap $nativeParameter.ParameterType)) "$context original argument type mismatch."
        }
    }
}
Assert-Binding ($patchCount -gt 0) 'No compiled Harmony patches found.'
Write-Output "Compiled Harmony bindings passed: $patchCount patch methods, $script:assertions assertions; metadata only."
