[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if(-not $Destination){$Destination=Join-Path (Split-Path $root -Parent) ('LightReader-source-handoff-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip')}
if(Test-Path -LiteralPath $Destination){throw 'Destination already exists.'}
Add-Type -AssemblyName System.IO.Compression
$files=@()
foreach($folder in @('src','tests')){
    $files+=Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
}
foreach($name in @('README.md','AUDIO-README.md','FREE-VOICE-README.md','docs/HANDOFF-2026-11-13.md')){$files+=Get-Item -LiteralPath (Join-Path $root $name)}
$files+=Get-ChildItem -LiteralPath (Join-Path $root 'tools') -File | Where-Object { $_.Extension -eq '.ps1' }
$files+=Get-ChildItem -LiteralPath $root -File -Filter '*.cmd'
$manifest=@()
$stream=[System.IO.File]::Create($Destination)
$zip=[System.IO.Compression.ZipArchive]::new($stream,[System.IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($file in $files | Sort-Object FullName -Unique){
        $relative=$file.FullName.Substring($root.Length+1).Replace('\','/')
        $manifest+=[pscustomobject]@{Path=$relative;Bytes=$file.Length;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,'LightReader/'+$relative,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    $entry=$zip.CreateEntry('LightReader/SHA256-MANIFEST.json')
    $writer=[System.IO.StreamWriter]::new($entry.Open(),[System.Text.UTF8Encoding]::new($false))
    try{$writer.Write(($manifest | ConvertTo-Json -Depth 3))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$check=[System.IO.Compression.ZipFile]::OpenRead($Destination)
try{
    foreach($item in $manifest){
        $entry=$check.GetEntry('LightReader/'+$item.Path);$input=$entry.Open();$sha=[System.Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($input)).Replace('-','');if($hash -ne $item.SHA256){throw "Archive hash mismatch: $($item.Path)"}}finally{$sha.Dispose();$input.Dispose()}
    }
}finally{$check.Dispose()}
$checksum=(Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
[System.IO.File]::WriteAllText($Destination+'.sha256',$checksum+'  '+[System.IO.Path]::GetFileName($Destination))
Write-Output "Verified $($manifest.Count) source/document files: $Destination"
Write-Output "SHA256: $checksum"
