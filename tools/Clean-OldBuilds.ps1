[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$release = Join-Path $root 'release'
$buildRoots = @('src/bin', 'src/obj', 'tests/bin', 'tests/obj')

function Assert-SafeDirectory([string]$path, [string]$parent) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $boundary = [System.IO.Path]::GetFullPath($parent).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete outside expected directory: $resolved"
    }
    $item = Get-Item -LiteralPath $resolved
    if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw "Refusing linked directory: $resolved"
    }
    $links = Get-ChildItem -LiteralPath $resolved -Recurse -Force -Attributes ReparsePoint
    if ($links) { throw "Refusing directory containing filesystem links: $resolved" }
    return $resolved
}

$versions = @(Get-ChildItem -LiteralPath $release -Directory | Where-Object {
    $_.Name -like 'LightReader-*' -and
    (Test-Path -LiteralPath (Join-Path $_.FullName 'LightReader.exe')) -and
    (Test-Path -LiteralPath (Join-Path $_.FullName 'LightReader.dll')) -and
    (Test-Path -LiteralPath (Join-Path $_.FullName 'LightReader.runtimeconfig.json'))
} | Sort-Object @{Expression={
    (Get-Item -LiteralPath (Join-Path $_.FullName 'LightReader.dll')).LastWriteTimeUtc
};Descending=$true}, @{Expression={$_.Name};Descending=$false})
if ($versions.Count -eq 0) { throw 'No complete release found; nothing will be deleted.' }
$latest = $versions[0]
Write-Output "Keeping: $($latest.FullName)"

# An unreadable process path must not allow deletion of a potentially running build.
$running = @(Get-Process -Name LightReader -ErrorAction SilentlyContinue)
foreach ($process in $running) {
    if (-not $process.Path) { throw "Cannot inspect LightReader process $($process.Id); cleanup aborted." }
}
$targets = @($versions | Select-Object -Skip 1 | ForEach-Object {
    Assert-SafeDirectory $_.FullName $release
})
foreach ($relative in $buildRoots) {
    $path = Join-Path $root $relative
    if (Test-Path -LiteralPath $path) {
        $targets += Assert-SafeDirectory $path (Split-Path -Parent $path)
    }
}

# Keep the normal launch entry aligned with the retained release.
$launcher = Join-Path $root ([string]::Concat([char]0x542F,[char]0x52A8,[char]0x8F7B,[char]0x8BFB,'.cmd'))
if ($PSCmdlet.ShouldProcess($launcher, 'Point launcher at latest release')) {
    $content = '@echo off' + "`r`n" + 'start "" "%~dp0release\' + $latest.Name + '\LightReader.exe"' + "`r`n"
    [System.IO.File]::WriteAllText($launcher, $content, [System.Text.Encoding]::ASCII)
}
$deleted = 0
$skipped = 0
foreach ($target in $targets) {
    $prefix = $target.TrimEnd('\') + '\'
    if ($running | Where-Object { $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }) {
        Write-Warning "Running build skipped. Close it and rerun cleanup: $target"
        $skipped++
        continue
    }
    if ($PSCmdlet.ShouldProcess($target, 'Delete old build directory')) {
        Remove-Item -LiteralPath $target -Recurse -Force
        $deleted++
        Write-Output "Deleted: $target"
    }
}
Write-Output "Deleted directories: $deleted; running directories skipped: $skipped"
