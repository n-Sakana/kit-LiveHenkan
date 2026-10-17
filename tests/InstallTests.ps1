param([Parameter(Mandatory=$true)][string]$Root)
# Install, start from the registered menu command, stop, uninstall. Checks that nothing stays behind.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$keys=@('Software\Classes\DesktopBackground\Shell\LiveHenkan','Software\Classes\Directory\Background\shell\LiveHenkan')
$checks=New-Object System.Collections.Generic.List[object]
function Check([string]$name,[bool]$ok,[string]$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=$ok;Detail=$detail});("{0} {1} {2}" -f $(if($ok){'ok  '}else{'FAIL'}),$name,$detail)}
function Registered {@($keys|Where-Object{$k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($_);if($k){$k.Dispose();$true}else{$false}})}
function Wait-Ping([bool]$expected,[int]$timeoutMs){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while($watch.ElapsedMilliseconds -lt $timeoutMs){if(($null -ne (Send-Resident 'ping' 150)) -eq $expected){return $watch.ElapsedMilliseconds};Start-Sleep -Milliseconds 100}
 return -1
}
# Refuse before entering cleanup: a failed precondition must never remove an
# existing registration or stop somebody else's resident.
if($null -ne (Send-Resident 'ping' 200)){throw 'A resident is already running; stop it first.'}
if((Registered).Count -ne 0){throw 'Existing registration; this test requires an initially unregistered machine.'}
try{
 Check 'nothing registered before' ((Registered).Count -eq 0)
 $out=& $ps -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'app\install.ps1') 2>&1|Out-String
 Check 'install exit code' ($LASTEXITCODE -eq 0) $out.Trim().Replace("`r`n",' / ')
 Check 'both menus registered' ((Registered).Count -eq 2)
 $commands=@{}
 foreach($key in $keys){
  $k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key)
  if($k){
   $commands[$key]=@($k.OpenSubKey('shell').GetSubKeyNames()|ForEach-Object{$k.OpenSubKey('shell\'+$_+'\command').GetValue('')})
   Check "menu title $key" ($k.GetValue('MUIVerb') -eq 'LiveHenkan') ([string]$k.GetValue('MUIVerb'))
   $shell=$k.OpenSubKey('shell')
   try{Check "submenu row count $key" ($shell.SubKeyCount -eq 4 -and $shell.SubKeyCount -le 16)}finally{$shell.Dispose()}
   $k.Dispose()
  }
 }
 $on=@($commands[$keys[0]]|Where-Object{$_ -match ' on$'})[0]
 $off=@($commands[$keys[0]]|Where-Object{$_ -match ' off$'})[0]
 Check 'on and off commands present' ([bool]$on -and [bool]$off) "$on"
 Check 'commands point into this folder' ($on -like ('*'+(Join-Path $Root 'app\LiveHenkan.vbs')+'*'))
 # The same command line Explorer runs for the menu entry.
 $null=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\wscript.exe') -ArgumentList ('"'+(Join-Path $Root 'app\LiveHenkan.vbs')+'" on') -PassThru -WindowStyle Hidden
 $startMs=Wait-Ping $true 40000
 Check 'resident answers after menu on' ($startMs -ge 0) "$startMs ms"
 $state=Get-ResidentState
 Check 'starts in kana mode' ($state['mode'] -eq 'Kana')
 $firstResidents=@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'"|Where-Object{$_.CommandLine -like '*LiveHenkan.ps1*'})
 $null=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\wscript.exe') -ArgumentList ('"'+(Join-Path $Root 'app\LiveHenkan.vbs')+'" on') -PassThru -WindowStyle Hidden
 # The second command itself is a short-lived PowerShell process. Allow it to
 # hand off and exit, then require the original resident PID (not just a count).
 Start-Sleep -Milliseconds 1500
 $handoff=[Diagnostics.Stopwatch]::StartNew()
 do{
  $remaining=@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'"|Where-Object{$_.CommandLine -like '*LiveHenkan.ps1*'})
  $sameResident=$firstResidents.Count -eq 1 -and $remaining.Count -eq 1 -and $remaining[0].ProcessId -eq $firstResidents[0].ProcessId
  if($sameResident){break}
  Start-Sleep -Milliseconds 250
 }while($handoff.ElapsedMilliseconds -lt 10000)
 Check 'second on keeps the same resident' $sameResident ("before={0}; after={1}; handoff wait={2} ms" -f ($firstResidents.ProcessId -join ','),($remaining.ProcessId -join ','),$handoff.ElapsedMilliseconds)
 $null=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\wscript.exe') -ArgumentList ('"'+(Join-Path $Root 'app\LiveHenkan.vbs')+'" off') -PassThru -WindowStyle Hidden
 $stopMs=Wait-Ping $false 10000
 Check 'resident gone after menu off' ($stopMs -ge 0) "$stopMs ms"
 Start-Sleep -Milliseconds 1500
 Check 'no resident process left' (@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'"|Where-Object{$_.CommandLine -like '*LiveHenkan.ps1*'}).Count -eq 0)
 # Uninstall while running must stop it too.
 $null=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\wscript.exe') -ArgumentList ('"'+(Join-Path $Root 'app\LiveHenkan.vbs')+'" on') -PassThru -WindowStyle Hidden
 Check 'resident answers again' ((Wait-Ping $true 40000) -ge 0)
 $out=& $ps -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'app\uninstall.ps1') 2>&1|Out-String
 Check 'uninstall exit code' ($LASTEXITCODE -eq 0) $out.Trim().Replace("`r`n",' / ')
 Check 'nothing registered after uninstall' ((Registered).Count -eq 0)
 Check 'uninstall stopped the resident' ((Wait-Ping $false 8000) -ge 0)
 Start-Sleep -Milliseconds 1500
 Check 'no resident process left after uninstall' (@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'"|Where-Object{$_.CommandLine -like '*LiveHenkan.ps1*'}).Count -eq 0)
}catch{
 Check 'unexpected error' $false ($_|Out-String)
}finally{
 # Leave the machine as it was found, whatever happened above.
 if($null -ne (Send-Resident 'ping' 200)){$null=Send-Resident 'quit'}
 foreach($key in $keys){$i=$key.LastIndexOf('\');$parent=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key.Substring(0,$i),$true);if($parent){$parent.DeleteSubKeyTree($key.Substring($i+1),$false);$parent.Dispose()}}
}
$failed=@($checks|Where-Object{!$_.Ok}).Count
[ordered]@{Root=$Root;Checks=$checks;Failed=$failed;At=(Get-Date).ToString('s')}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results 'install-tests.json')
"install tests: $($checks.Count-$failed)/$($checks.Count)"
if($failed){exit 1}
