param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$Zip)
# Read-only validation of the exact archive to be carried to another machine.
$ErrorActionPreference='Stop'
$rootPath=[IO.Path]::GetFullPath($Root)
$manifest=Get-Content -LiteralPath (Join-Path $rootPath 'manifest.json') -Raw|ConvertFrom-Json
foreach($entry in $manifest){
 $path=Join-Path $rootPath $entry.Name
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256){throw ('Hash mismatch: '+$entry.Name)}
 if((Get-Item -LiteralPath $path).Length -ne $entry.Bytes){throw ('Size mismatch: '+$entry.Name)}
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Zip))
try{
 foreach($entry in $archive.Entries){
  $name=$entry.FullName.Replace('\','/')
  if(!$name.StartsWith('Henkan/') -or $name -match '(?i)\.(exe|dll)$|/(user|temp|results|classic|mixed|output)/'){throw ('Unexpected archive entry '+$name)}
  if($entry.Length -eq 0 -and $name.EndsWith('/')){continue}
  $local=Join-Path (Split-Path $rootPath -Parent) $entry.FullName
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
  if($hash -ne (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash){throw ('Archive hash mismatch '+$name)}
 }
 $count=$archive.Entries.Count
 if($count -ne $manifest.Count+1){throw 'Unexpected archive file count'}
}finally{$archive.Dispose()}
[ordered]@{ManifestFiles=$manifest.Count;ArchiveEntries=$count;SHA256=(Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash;Bytes=(Get-Item -LiteralPath $Zip).Length;At=(Get-Date).ToString('o')}|ConvertTo-Json
