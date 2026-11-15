Add-Type -AssemblyName System.Security
$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = 'LightReader - Audio API Key'
Write-Host 'Paste the Audio Generation API Key, then press Enter.'
Write-Host 'Input is hidden. The key is saved encrypted for this Windows user.'
$secret = Read-Host 'API Key' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try {
    $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'Empty key; nothing saved.' }
    $dir = Join-Path $env:LOCALAPPDATA 'LightReader'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    $encrypted = [Security.Cryptography.ProtectedData]::Protect($bytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $dir 'audio-key.dat'), $encrypted)
    [Array]::Clear($bytes, 0, $bytes.Length)
    $value = $null
    Write-Host 'Saved successfully. You can close this window.'
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    $secret.Dispose()
}
Read-Host 'Press Enter to close'
