param([Parameter(Mandatory=$true)][string]$StageDirectory,[Parameter(Mandatory=$true)][string]$ExpectedHash)
$ErrorActionPreference='Stop'
# A desktop child process can inherit PowerShell 7 module search paths.
# Load the matching Windows PowerShell modules explicitly before validation.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$installed='C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE'
$dllName='AgesOfCalradia.CoastSurfaceFix.dll'
$source=Join-Path $StageDirectory $dllName
$target=Join-Path $installed ('bin\Win64_Shipping_Client\'+$dllName)
$status=Join-Path $StageDirectory 'install-status.txt'
function Status([string]$Message){[IO.File]::WriteAllText($status,([DateTimeOffset]::Now.ToString('O')+' '+$Message))}
try {
    if((Get-FileHash -LiteralPath $source).Hash -ne $ExpectedHash){throw 'Staged candidate hash mismatch.'}
    Status 'WAITING: one-time installation waits for Bannerlord and launcher to close; no game files changed.'
    $deadline=[DateTime]::UtcNow.AddHours(6)
    while(@(Get-Process 'Bannerlord*','TaleWorlds*' -ErrorAction SilentlyContinue).Count){
        if([DateTime]::UtcNow -ge $deadline){throw 'Wait expired after six hours; installed version remains unchanged.'}
        Start-Sleep -Seconds 5
    }
    & (Join-Path $root 'Tests\Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $installed
    $fill=Join-Path $installed 'bin\Win64_Shipping_Client\AgesOfCalradia.PoliticalFillSeamFix.dll'
    $fillHash='0918C5DBB2AD59BFAB9F768EE781D9F86A6DDDDE342D6D76A2BFA52EB88F063E'
    if((Get-FileHash -LiteralPath $fill).Hash -ne $fillHash){throw 'Accepted fill hash changed.'}
    $manifest=Join-Path $installed 'SubModule.xml'
    $manifestHash=(Get-FileHash -LiteralPath $manifest).Hash
    [xml]$xml=Get-Content -LiteralPath $manifest
    if(@($xml.Module.SubModules.SubModule | Where-Object {$_.DLLName.value -eq $dllName}).Count -ne 1){throw 'Expected installed coast registration changed.'}
    if(@(Get-Process 'Bannerlord*','TaleWorlds*' -ErrorAction SilentlyContinue).Count){throw 'Game restarted during validation; update deferred without mutation.'}
    $backup=Join-Path $StageDirectory 'previous-CoastSurfaceFix.dll'
    Copy-Item -LiteralPath $target -Destination $backup
    $beforeHash=(Get-FileHash -LiteralPath $backup).Hash
    $writer=$null;$mutated=$false
    try {
        # Obtain exclusive file access before any write; a newly loaded DLL
        # makes this fail rather than modify an assembly the game is using.
        $writer=[IO.File]::Open($target,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $bytes=[IO.File]::ReadAllBytes($source)
        $mutated=$true;$writer.SetLength(0);$writer.Write($bytes,0,$bytes.Length);$writer.Flush($true);$writer.Dispose();$writer=$null
        if((Get-FileHash -LiteralPath $target).Hash -ne $ExpectedHash){throw 'Installed hash readback failed.'}
        if((Get-FileHash -LiteralPath $manifest).Hash -ne $manifestHash){throw 'Manifest changed during installation.'}
        & (Join-Path $root 'Tests\Verify-ProtectedPoliticalBaseline.ps1') -InstalledModuleRoot $installed
        if((Get-FileHash -LiteralPath $fill).Hash -ne $fillHash){throw 'Accepted fill changed during installation.'}
    } catch {
        if($writer){$writer.Dispose();$writer=$null}
        if($mutated){
            Copy-Item -LiteralPath $backup -Destination $target
            if((Get-FileHash -LiteralPath $target).Hash -ne $beforeHash){throw 'Rollback hash failed; inspect staged backup.'}
        }
        throw
    }
    Status ('INSTALLED: '+$ExpectedHash+'; previous DLL backed up; manifest/Core/prefab/fill unchanged; visual verification pending.')
} catch {Status ('FAILED: '+$_.Exception.Message);exit 1}
