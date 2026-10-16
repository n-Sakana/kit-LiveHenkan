param([string]$Destination='',[string]$ModelDir='',[switch]$Replace)
# Builds the folder that is carried to another PC: scripts, sources and the model. Run from the repo.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'app\Common.ps1')
# Get-FileHash is a script function that an inherited PSModulePath can hide, so the hash is computed directly.
function Get-Sha256([string]$path){
 $sha=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($path)
 try{return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
}
$model=Resolve-Model -Root $PSScriptRoot -ModelDir $ModelDir
if(!$Destination){$Destination=Join-Path $PSScriptRoot 'dist\LiveHenkan'}
$destinationPath=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $destinationPath){
 # Only a folder this script made earlier is ever replaced.
 if(!$Replace -or !(Test-Path -LiteralPath (Join-Path $destinationPath 'manifest.json') -PathType Leaf)){throw "Destination already exists; choose a new folder or pass -Replace: $destinationPath"}
 Remove-Item -LiteralPath $destinationPath -Recurse -Force
}
$files=@('install.bat','uninstall.bat','THIRD-PARTY.txt')
$files+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'app') -File|ForEach-Object{'app\'+$_.Name})
$files+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'app\src') -Filter '*.cs' -File|ForEach-Object{'app\src\'+$_.Name})
foreach($name in $files){if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $name) -PathType Leaf)){throw "Missing source: $name"}}
[IO.Directory]::CreateDirectory((Join-Path $destinationPath 'app\src'))|Out-Null
foreach($name in $files){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destinationPath $name)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Portable-README.txt') -Destination (Join-Path $destinationPath 'README.txt')
$copied=Join-Path $destinationPath 'xsmall.jinen'
Copy-Item -LiteralPath $model -Destination $copied
if((Get-Sha256 $model) -ne (Get-Sha256 $copied)){throw 'Copied model hash mismatch'}
$manifest=@(Get-ChildItem -LiteralPath $destinationPath -File -Recurse|Sort-Object FullName|ForEach-Object{
 [pscustomobject]@{Name=$_.FullName.Substring($destinationPath.Length+1);Bytes=$_.Length;SHA256=(Get-Sha256 $_.FullName)}})
$manifest|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $destinationPath 'manifest.json')
# Nothing that needs installing may travel in the folder.
$binaries=@(Get-ChildItem -LiteralPath $destinationPath -File -Recurse|Where-Object{$_.Extension -in '.exe','.dll'})
if($binaries.Count -gt 0){throw ('Unexpected binaries: '+($binaries.Name -join ', '))}
Write-Output "Created: $destinationPath ($($manifest.Count) files)"
