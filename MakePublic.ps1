param([string]$Source='',[string]$Destination='')
# Builds the folder for publishing: Henkan.zip (the carried folder) and README.md. Nothing is uploaded here.
$ErrorActionPreference='Stop'
if(!$Source){$Source=Join-Path $PSScriptRoot 'dist\Henkan'}
$sourcePath=[IO.Path]::GetFullPath($Source)
if(!(Test-Path -LiteralPath (Join-Path $sourcePath 'manifest.json') -PathType Leaf)){throw "Not a folder made by MakePortable.ps1: $sourcePath"}
if(!$Destination){
 $base=Join-Path $PSScriptRoot ('dist\public-Henkan-'+(Get-Date -Format 'yyyyMMdd'))
 $Destination=$base;$n=2
 while(Test-Path -LiteralPath $Destination){$Destination="$base-$n";$n++}
}
$destinationPath=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $destinationPath){throw "Destination already exists; choose a new folder: $destinationPath"}
# Files the resident writes while it runs must not travel with the published copy.
foreach($name in 'user','temp','results'){if(Test-Path -LiteralPath (Join-Path $sourcePath $name)){throw "Remove '$name' from $sourcePath first (made by running it); or rebuild with MakePortable.ps1 -Replace."}}
[IO.Directory]::CreateDirectory($destinationPath)|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $destinationPath 'Henkan.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($sourcePath,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Public-README.md') -Destination (Join-Path $destinationPath 'README.md')
$bytes=(Get-Item -LiteralPath $zip).Length
if($bytes -ge 100MB){throw "Henkan.zip is $bytes bytes; it must stay under 100 MB"}
Write-Output ("Created: {0} (Henkan.zip {1:N1} MB, README.md)" -f $destinationPath,($bytes/1MB))
