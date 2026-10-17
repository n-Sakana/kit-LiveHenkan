param([switch]$Quiet)
# Adds the "live conversion" entries to the right-click menu of the desktop and of folder backgrounds.
# Writes only under HKEY_CURRENT_USER. No administrator rights, no files outside this folder.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
. (Join-Path $PSScriptRoot 'Menu.ps1')
$s=Get-Strings
try{
 $root=Split-Path -Parent $PSScriptRoot
 Register-HenkanMenu -App $PSScriptRoot -Strings $s
 if(!$Quiet){
  Write-Host ''
  Write-Host $s['install.done'] -ForegroundColor Green
  Write-Host $s['install.where']
  Write-Host $s['install.win11']
  if(!(Test-Path -LiteralPath (Join-Path $root 'xsmall.jinen') -PathType Leaf)){Write-Host '';Write-Host $s['install.model.missing'] -ForegroundColor Yellow}
  Write-Host ''
 }
 exit 0
}catch{
 Write-Host ''
 Write-Host ($s['install.failed']+' '+$_.Exception.Message) -ForegroundColor Red
 exit 1
}
