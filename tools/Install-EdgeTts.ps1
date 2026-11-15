$ErrorActionPreference = 'Stop'
# Optional dependency only. No API key or reader content is used here.
$readerEnv = Join-Path $env:LOCALAPPDATA 'LightReader\edge-tts-env'
$readerPython = Join-Path $readerEnv 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $readerPython)) {
    $readerBase = Get-Command python -ErrorAction SilentlyContinue
    if (-not $readerBase -or $readerBase.Source -like "*WindowsApps*") { throw 'Install Python 3.10+ x64 from https://www.python.org/downloads/windows/ and reopen PowerShell.' }
    & $readerBase.Source -c "import sys; assert sys.version_info >= (3,10)"
    if ($LASTEXITCODE -ne 0) { throw 'Python version check failed.' }
    & $readerBase.Source -m venv $readerEnv
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the private Python environment.' }
}
& $readerPython -m pip install 'edge-tts==7.3.1'
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed. Check pip/network configuration.' }
& $readerPython -c "import edge_tts, importlib.metadata; print(importlib.metadata.version('edge-tts'))"
if ($LASTEXITCODE -ne 0) { throw 'Dependency verification failed.' }
Write-Host "Private environment: $readerPython"
Write-Host 'Restart LightReader and select Edge natural speech. Online synthesis sends text to Microsoft.'
