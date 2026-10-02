param([string]$AssemblyPath = (Join-Path $PSScriptRoot 'bin\Release\AgesOfCalradiaInternalWarsTest.dll'))
$ErrorActionPreference = 'Stop'

# Only the plain nested snapshot formatter runs. No Bannerlord objects, campaign capture,
# file writer, game process, or live mission are touched by this verification.
$diagnosticAssembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$diagnosticType = $diagnosticAssembly.GetType('AgesOfCalradiaInternalWarsTest.InternalWarDiagnosticsReport+DiagnosticSnapshot', $true)
$diagnosticFlags = [Reflection.BindingFlags]'Instance,NonPublic'
$diagnosticAdd = $diagnosticType.GetMethod('Add', $diagnosticFlags)
$diagnosticViolation = $diagnosticType.GetMethod('Violation', $diagnosticFlags)
$diagnosticRead = $diagnosticType.GetMethod('Read', $diagnosticFlags)
$diagnosticRender = $diagnosticType.GetMethod('Render', $diagnosticFlags)
$diagnosticCheck = $diagnosticType.GetMethod('Check', $diagnosticFlags)
$diagnosticCount = 0

function Assert-Diagnostic([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:diagnosticCount++
}

$diagnosticSnapshot = [Activator]::CreateInstance($diagnosticType, $true)
$diagnosticText = [string]$diagnosticRender.Invoke($diagnosticSnapshot, @())
Assert-Diagnostic ($diagnosticText.Contains('snapshot.complete=True')) 'Empty snapshot must report complete reads.'
Assert-Diagnostic ($diagnosticText.Contains('invariant_findings.count=0')) 'Empty snapshot must report zero findings.'
Assert-Diagnostic ($diagnosticText.Contains('unobserved invariants may still fail')) 'Formatter must not imply that zero findings proves safety.'

$null = $diagnosticAdd.Invoke($diagnosticSnapshot, @("reason`r`nINJECT", "line1`nline2`tvalue"))
$null = $diagnosticAdd.Invoke($diagnosticSnapshot, @('null_value', $null))
$null = $diagnosticAdd.Invoke($diagnosticSnapshot, @('long_value', ('x' * 900)))
$diagnosticPreviousCulture = [Globalization.CultureInfo]::CurrentCulture
try {
    [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
    $null = $diagnosticAdd.Invoke($diagnosticSnapshot, @('decimal_value', [double]12.5))
} finally { [Globalization.CultureInfo]::CurrentCulture = $diagnosticPreviousCulture }
$diagnosticText = [string]$diagnosticRender.Invoke($diagnosticSnapshot, @())
Assert-Diagnostic ($diagnosticText.Contains('reason  INJECT=line1 line2 value')) 'Control characters must not create forged report lines.'
Assert-Diagnostic ($diagnosticText.Contains('null_value=(empty)')) 'Null values must remain explicit.'
Assert-Diagnostic ($diagnosticText.Contains('long_value=' + ('x' * 512) + ' [truncated]')) 'Long values must be bounded and marked.'
Assert-Diagnostic (-not $diagnosticText.Contains('x' * 513)) 'Formatter must not retain unbounded text.'
Assert-Diagnostic ($diagnosticText.Contains('decimal_value=12.5')) 'Report values must use invariant culture.'

$null = $diagnosticViolation.Invoke($diagnosticSnapshot, @("capture`nstate mismatch"))
$diagnosticFailure = [Action]{ throw [InvalidOperationException]::new('simulated native read failure') }
$null = $diagnosticRead.Invoke($diagnosticSnapshot, @('native-section', $diagnosticFailure))
$diagnosticText = [string]$diagnosticRender.Invoke($diagnosticSnapshot, @())
Assert-Diagnostic ($diagnosticText.Contains('snapshot.complete=False')) 'A failed read must mark the snapshot incomplete.'
Assert-Diagnostic ($diagnosticText.Contains('read_errors.count=1')) 'A failed read must be counted.'
Assert-Diagnostic ($diagnosticText.Contains('UNAVAILABLE: native-section:') -and $diagnosticText.Contains('simulated native read failure')) 'Failed read evidence must be visible.'
Assert-Diagnostic ($diagnosticText.Contains('invariant_findings.count=1') -and $diagnosticText.Contains('INVARIANT: capture state mismatch')) 'Invariant findings must be distinct and single-line.'
Assert-Diagnostic ($diagnosticText.Contains('decimal_value=12.5')) 'A failed section must retain previous successful observations.'

$null = $diagnosticCheck.Invoke($diagnosticSnapshot, @('war.test.capture_owner', $true, 'observed'))
$null = $diagnosticCheck.Invoke($diagnosticSnapshot, @('war.test.cleanup_residue', $false, "review`nINJECT|payload"))
$null = $diagnosticCheck.Invoke($diagnosticSnapshot, @('manual.ui_clicks', $null, 'not played'))
$diagnosticText = [string]$diagnosticRender.Invoke($diagnosticSnapshot, @())
Assert-Diagnostic ($diagnosticText.Contains('CHECK war.test.capture_owner|OBSERVED_OK|observed')) 'Observed checks not formatted.'
Assert-Diagnostic ($diagnosticText.Contains('CHECK war.test.cleanup_residue|REVIEW|review INJECT/payload')) 'Review fields allow forged lines or delimiters.'
Assert-Diagnostic ($diagnosticText.Contains('CHECK manual.ui_clicks|NOT_EXERCISED|not played')) 'Unobserved scenario became a pass.'
Write-Output "Internal-war diagnostic formatter: $diagnosticCount checks passed. No native campaign calls executed."
