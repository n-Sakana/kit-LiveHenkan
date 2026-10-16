param([switch]$Quiet)
# Stops the resident if it is running and removes the right-click menu entries. Touches only HKEY_CURRENT_USER.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
. (Join-Path $PSScriptRoot 'Menu.ps1')
$s=Get-Strings
try{
 if($null -ne (Send-Resident 'quit')){
  if(!$Quiet){Write-Host $s['uninstall.stopping']}
  for($i=0;$i -lt 40 -and $null -ne (Send-Resident 'ping' 100);$i++){Start-Sleep -Milliseconds 100}
 }
 $before=@(Get-LiveHenkanMenuKeys)
 Unregister-LiveHenkanMenu
 $after=@(Get-LiveHenkanMenuKeys)
 if($after.Count -gt 0){throw ('still registered: '+($after -join ', '))}
 if(!$Quiet){
  Write-Host ''
  if($before.Count -gt 0){Write-Host $s['uninstall.done'] -ForegroundColor Green}else{Write-Host $s['uninstall.none']}
  Write-Host $s['uninstall.kept']
  Write-Host ''
 }
 exit 0
}catch{
 Write-Host ''
 Write-Host ($s['uninstall.failed']+' '+$_.Exception.Message) -ForegroundColor Red
 exit 1
}
