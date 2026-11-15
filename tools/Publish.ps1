[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$destination=Join-Path $root 'release/LightReader-win-x64'
foreach($process in @(Get-Process LightReader -ErrorAction SilentlyContinue)){
    if(-not $process.Path){throw 'Cannot inspect running reader. Close it before publishing.'}
    if($process.Path.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){
        Write-Output "Stopping running fixed-release reader: $($process.Id)"
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
}
dotnet publish (Join-Path $root 'src/LightReader.csproj') -c Release -r win-x64 --self-contained true -o $destination --nologo
if($LASTEXITCODE -ne 0){throw 'Publish failed; existing releases retained.'}
& (Join-Path $PSScriptRoot 'Clean-OldBuilds.ps1')
