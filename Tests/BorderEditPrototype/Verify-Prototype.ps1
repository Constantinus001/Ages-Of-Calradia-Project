param(
    [string]$PublishedAssets='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE\AuthoredBorders'
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$repository=Split-Path -Parent (Split-Path -Parent $root)
& dotnet msbuild (Join-Path $root 'BorderEditPrototype.csproj') /t:Build /p:Configuration=Release /nologo /v:minimal
if($LASTEXITCODE -ne 0){throw 'Prototype Release build failed.'}
& dotnet msbuild (Join-Path $root 'Tests\GraphTests.csproj') /t:Build /p:Configuration=Release /nologo /v:minimal
if($LASTEXITCODE -ne 0){throw 'Behavior verifier build failed.'}
& (Join-Path $root 'Tests\bin\Release\BorderGraphTests.exe') (Join-Path $root 'Tests\Fixtures\ReviewedExactGraph.topology') $PublishedAssets
if($LASTEXITCODE -ne 0){throw 'Behavior verifier failed.'}
& dotnet msbuild (Join-Path $root 'Tests\PreviewTests.csproj') /t:Build /p:Configuration=Release /nologo /v:minimal
if($LASTEXITCODE -ne 0){throw 'Native adapter verifier build failed.'}
& (Join-Path $root 'Tests\bin\Preview\BorderPreviewTests.exe')
if($LASTEXITCODE -ne 0){throw 'Native adapter verifier failed.'}
$windowsPowerShell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Tests\Verify-NativeBindings.ps1')
if($LASTEXITCODE -ne 0){throw 'Native binding verifier failed.'}
[xml]$manifest=Get-Content -LiteralPath (Join-Path $root 'SubModule.xml') -Raw
[xml]$prefab=Get-Content -LiteralPath (Join-Path $root 'GUI\Prefabs\AocBorderEditPrototype.xml') -Raw
if($manifest.Module.Id.value -ne 'AgesOfCalradiaBorderEditPrototype'){throw 'Incorrect module identity.'}
$commands=@($prefab.SelectNodes('//*[@Command.Click]') | ForEach-Object { $_.GetAttribute('Command.Click') })
foreach($name in @('ExecuteBorderFill','ExecuteCoast','ExecutePolitical','ExecuteView','ExecuteAdd','ExecuteFill','ExecuteDelete','ExecuteConnect','ExecuteUndo','ExecuteRedo','ExecuteSave','ExecuteFinish','ExecuteCancel')) {
 if($name -notin $commands){throw "Missing prototype command $name"}
}
$runtime=Get-Content -LiteralPath (Join-Path $root 'PrototypeRuntime.cs') -Raw
if($runtime.Contains('if(!PublishedBorderLayout.Enabled && ownsInput && Input.IsKeyReleased(InputKey.F9))') -or
   $runtime.Contains('if(PublishedBorderLayout.Enabled) return;') -or
   -not $runtime.Contains('PUBLISHED LAYOUT SAVED:') -or
   -not $runtime.Contains('ApplyPublishedBeforeFirstMapFrame(15000d)') -or
   -not $runtime.Contains('_repair.ApplyBeforeFirstMapFrame') -or
   -not $runtime.Contains('if(hasPublishedDraft&&LoadingPublication)')) {
    throw 'Published Core layouts must remain editable with F9 and report direct saves.'
}
& (Join-Path $repository 'Tests\Verify-ProtectedPoliticalBaseline.ps1')
& (Join-Path $repository 'Tests\Verify-PublishedPoliticalBorderDeployment.ps1')
Write-Output 'PASS: prototype package, UI command wiring, published-layout load contract, and protected artifacts verified.'
