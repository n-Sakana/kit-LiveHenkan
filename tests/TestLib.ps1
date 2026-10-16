# Shared by the tests. ASCII only; expected Japanese text is kept in data files.
$ErrorActionPreference='Stop'
$script:Repo=Split-Path -Parent $PSScriptRoot
$script:Results=Join-Path $script:Repo 'results'
. (Join-Path $script:Repo 'app\Common.ps1')
[Console]::OutputEncoding=[Text.Encoding]::UTF8
if($PSVersionTable.PSVersion.Major -ne 5 -or ![Environment]::Is64BitProcess){throw 'Run the tests with 64-bit Windows PowerShell 5.1.'}
[IO.Directory]::CreateDirectory($script:Results)|Out-Null

function Initialize-Tests {
 if(!('LiveHenkan.Tests.Driver' -as [type])){
  $temp=Join-Path $script:Repo 'temp';[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp
  $null=Build-LiveHenkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'Driver.cs'))
  [LiveHenkan.Native]::PrepareProcess()
 }
}

function Get-TestModelDir([string]$ModelDir){
 if($ModelDir){return $ModelDir}
 if($env:JINEN_MODEL_DIR){return $env:JINEN_MODEL_DIR}
 if(Test-Path -LiteralPath (Join-Path $script:Repo 'xsmall.jinen')){return $script:Repo}
 throw 'Model folder not given. Use -ModelDir or JINEN_MODEL_DIR.'
}

# Starts the resident from $Root (a repo or a distributed folder) in test mode and waits until it answers.
function Start-Resident([string]$Root,[string]$ModelDir,[string]$UserDir='',[switch]$Real){
 if($null -ne (Send-Resident 'ping' 200)){throw 'A resident is already running; stop it first.'}
 $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $arguments='-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -Command on' -f (Join-Path $Root 'app\LiveHenkan.ps1')
 # -Real leaves out the test mode: no tagged test input is accepted and the test-only watch thread does not run.
 if(!$Real){$arguments+=' -Test'}
 if($ModelDir){$arguments+=' -ModelDir "{0}"' -f $ModelDir}
 # Tests keep their words and settings away from the real ones.
 if(!$UserDir){$UserDir=Join-Path $script:Results 'test-user'}
 [IO.Directory]::CreateDirectory($UserDir)|Out-Null
 $arguments+=' -UserDir "{0}"' -f $UserDir
 $started=[Diagnostics.Stopwatch]::StartNew()
 if($env:LH_DETACHED -eq '1'){
  # Started through WMI, the resident is a child of the WMI host: outside whatever job, priority or limits this shell runs under,
  # which is how it runs when the user starts it from the right-click menu.
  $created=Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=('"'+$ps+'" '+$arguments)}
  if($created.ReturnValue -ne 0){throw "Could not start the resident through WMI: $($created.ReturnValue)"}
  $process=Get-Process -Id $created.ProcessId
 }else{
  $process=Start-Process -FilePath $ps -ArgumentList $arguments -PassThru -WindowStyle Hidden
 }
 while($null -eq (Send-Resident 'ping' 150)){
  if($process.HasExited){
   $errorFile=Join-Path $Root 'user\error.txt'
   $detail=if(Test-Path -LiteralPath $errorFile){[IO.File]::ReadAllText($errorFile,[Text.Encoding]::UTF8)}else{''}
   throw "Resident exited with code $($process.ExitCode). $detail"
  }
  if($started.ElapsedMilliseconds -gt 40000){Stop-Process -Id $process.Id -Force;throw 'Resident did not start in 40 s'}
  Start-Sleep -Milliseconds 100
 }
 return [pscustomobject]@{Process=$process;StartMs=$started.ElapsedMilliseconds}
}
function Stop-Resident($resident){
 $null=Send-Resident 'quit'
 if($resident -and $resident.Process){
  if(!$resident.Process.WaitForExit(6000)){Stop-Process -Id $resident.Process.Id -Force -ErrorAction SilentlyContinue;return 'killed'}
  return 'exited'
 }
 return 'none'
}
function Get-ResidentState {
 $line=Send-Resident 'state' 1000
 if($null -eq $line){throw 'Resident is not answering'}
 $map=@{}
 foreach($part in $line.Split("`t")){$i=$part.IndexOf('=');if($i -gt 0){$map[$part.Substring(0,$i)]=$part.Substring($i+1)}}
 return $map
}
function Get-ResidentMetrics {
 $line=Send-Resident 'metrics' 1000
 $map=@{}
 foreach($part in $line.Split("`t")){$i=$part.IndexOf('=');if($i -gt 0){$map[$part.Substring(0,$i)]=$part.Substring($i+1)}}
 $numbers={param($text) if($text){@($text.Split(',')|ForEach-Object{[double]::Parse($_,[Globalization.CultureInfo]::InvariantCulture)})}else{@()}}
 return [pscustomobject]@{Shown=(& $numbers $map['shown']);Converted=(& $numbers $map['converted']);Threads=$map['threads']}
}
function Get-Percentile($values,[double]$p){
 $sorted=@($values|Sort-Object)
 if($sorted.Count -eq 0){return $null}
 return [math]::Round($sorted[[math]::Min($sorted.Count-1,[int][math]::Floor($sorted.Count*$p))],1)
}
# Waits until the resident shows no composition, or until the view text equals $Text.
function Wait-Resident([scriptblock]$Until,[int]$TimeoutMs=3000){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while($watch.ElapsedMilliseconds -lt $TimeoutMs){
  $state=Get-ResidentState
  if(& $Until $state){return $state}
  Start-Sleep -Milliseconds 20
 }
 return (Get-ResidentState)
}

# A new Notepad of our own, empty or holding $Content. Refuses when a Notepad is already open, to stay away from
# the user's windows.
function Open-TestNotepad([string]$Content='') {
 $before=@(Get-Process notepad -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Id)
 if($before.Count -gt 0){throw 'A Notepad is already open; refusing to start, to protect its windows.'}
 $file=Join-Path $script:Results 'blank.txt'
 [IO.File]::WriteAllText($file,$Content,(New-Object Text.UTF8Encoding($false)))
 $null=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\notepad.exe') -ArgumentList ('"'+$file+'"') -PassThru
 $watch=[Diagnostics.Stopwatch]::StartNew();$new=@()
 while($watch.ElapsedMilliseconds -lt 8000){
  $new=@(Get-Process notepad -ErrorAction SilentlyContinue|Where-Object{$_.Id -notin $before -and $_.MainWindowHandle -ne 0})
  if($new.Count -eq 1){break}
  Start-Sleep -Milliseconds 100
 }
 if($new.Count -ne 1){throw 'Could not identify our own Notepad'}
 Start-Sleep -Milliseconds 500
 $hwnd=$new[0].MainWindowHandle.ToInt64()
 [LiveHenkan.Tests.Driver]::Move($hwnd,120,120,900,520)
 if(![LiveHenkan.Tests.Driver]::Activate($hwnd)){throw 'Could not activate our Notepad'}
 $field=[LiveHenkan.Tests.Driver]::FindClass($hwnd,'RichEditD2DPT')
 if(!$field){throw 'Notepad editor not found'}
 $field.SetFocus()
 Start-Sleep -Milliseconds 200
 return [pscustomobject]@{Pid=$new[0].Id;Hwnd=$hwnd;Field=$field}
}
function Read-TestNotepad($notepad){return ([LiveHenkan.Tests.Driver]::Read($notepad.Field)).TrimEnd([char[]]"`r`n")}
# The same with one line feed between lines, whatever the editor uses.
function Read-TestNotepadLines($notepad){return ([LiveHenkan.Tests.Driver]::Read($notepad.Field)).Replace("`r`n","`n").Replace("`r","`n")}
function Clear-TestNotepad($notepad){
 if([LiveHenkan.Tests.Driver]::Foreground() -ne $notepad.Hwnd){$null=[LiveHenkan.Tests.Driver]::Activate($notepad.Hwnd)}
 $notepad.Field.SetFocus()
 # Verify that Ctrl+A and Backspace cleared the text; retry with a longer interval if needed.
 for($try=0;$try -lt 4;$try++){
  [LiveHenkan.Tests.Driver]::OwnKey(65,$false,$true);if($try -gt 0){Start-Sleep -Milliseconds 150}
  [LiveHenkan.Tests.Driver]::OwnKey(8,$false,$false)
  Start-Sleep -Milliseconds 120
  if([LiveHenkan.Tests.Driver]::Read($notepad.Field).Trim([char[]]"`r`n").Length -eq 0){return}
  Start-Sleep -Milliseconds 200
  if([LiveHenkan.Tests.Driver]::Read($notepad.Field).Trim([char[]]"`r`n").Length -eq 0){return}
 }
}
function Close-TestNotepad($notepad){
 if(!$notepad){return}
 # Emptied and saved to its own file, the tab has nothing unsaved; closing the tab itself (Ctrl+W) leaves nothing
 # for Notepad to bring back the next time it is started.
 try{
  Clear-TestNotepad $notepad
  [LiveHenkan.Tests.Driver]::OwnKey(0x53,$false,$true);Start-Sleep -Milliseconds 300
  [LiveHenkan.Tests.Driver]::OwnKey(0x57,$false,$true);Start-Sleep -Milliseconds 500
 }catch{}
 $process=Get-Process -Id $notepad.Pid -ErrorAction SilentlyContinue
 if($process -and !$process.HasExited){[LiveHenkan.Tests.Driver]::Close($notepad.Hwnd)}
 Start-Sleep -Milliseconds 600
 $process=Get-Process -Id $notepad.Pid -ErrorAction SilentlyContinue
 if($process -and !$process.HasExited){Stop-Process -Id $notepad.Pid -Force -ErrorAction SilentlyContinue}
}
function Save-WindowShot($hwnd,[string]$name){
 $r=[LiveHenkan.Tests.Driver]::Rect($hwnd)
 $path=Join-Path $script:Results $name
 [LiveHenkan.Tests.Driver]::Capture($r[0],$r[1],$r[2]-$r[0],$r[3]-$r[1],$path)
 return $path
}

# ---- applications of our own ------------------------------------------
# Every Open-Test* returns an object with Kind, Pid, Hwnd and the script blocks Read, Reset and Close.
function New-TestApp([string]$Kind,[int]$ProcessId,[long]$Hwnd,[scriptblock]$Read,[scriptblock]$Reset,[scriptblock]$Close,$State){
 return [pscustomobject]@{Kind=$Kind;Pid=$ProcessId;Hwnd=$Hwnd;Read=$Read;Reset=$Reset;Close=$Close;State=$State}
}
function Open-TestApp([string]$Kind){
 switch($Kind){
  'notepad'{
   $n=Open-TestNotepad
   return New-TestApp 'notepad' $n.Pid $n.Hwnd {param($a) Read-TestNotepad $a.State} {param($a) Clear-TestNotepad $a.State} {param($a) Close-TestNotepad $a.State} $n
  }
  'word'{
   $before=@(Get-Process WINWORD -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Id)
   if($before.Count -gt 0){throw 'Word is already open; refusing to start, to protect its documents.'}
   $word=New-Object -ComObject Word.Application
   $new=@(Get-Process WINWORD -ErrorAction SilentlyContinue|Where-Object{$_.Id -notin $before})
   if($new.Count -ne 1){throw 'Could not identify our own Word'}
   $doc=$word.Documents.Add();$word.Visible=$true
   $hwnd=[long]$doc.ActiveWindow.Hwnd
   [LiveHenkan.Tests.Driver]::Move($hwnd,120,120,1000,640)
   if(![LiveHenkan.Tests.Driver]::Activate($hwnd)){throw 'Could not activate our Word'}
   Start-Sleep -Milliseconds 800
   return New-TestApp 'word' $new[0].Id $hwnd {param($a) ([string]$a.State.Doc.Content.Text).TrimEnd([char[]]"`r`n")} {param($a) $a.State.Doc.Content.Text='';$a.State.Word.Selection.SetRange(0,0);Start-Sleep -Milliseconds 150} {
    param($a)
    try{$a.State.Doc.Close(0)}catch{}
    try{$a.State.Word.Quit()}catch{}
    try{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($a.State.Doc);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($a.State.Word)}catch{}
    Start-Sleep -Milliseconds 800
    $p=Get-Process -Id $a.Pid -ErrorAction SilentlyContinue;if($p -and !$p.HasExited){Stop-Process -Id $a.Pid -Force -ErrorAction SilentlyContinue}
   } ([pscustomobject]@{Word=$word;Doc=$doc})
  }
  'excel'{
   $before=@(Get-Process EXCEL -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Id)
   if($before.Count -gt 0){throw 'Excel is already open; refusing to start, to protect its workbooks.'}
   $excel=New-Object -ComObject Excel.Application
   $new=@(Get-Process EXCEL -ErrorAction SilentlyContinue|Where-Object{$_.Id -notin $before})
   if($new.Count -ne 1){throw 'Could not identify our own Excel'}
   $excel.DisplayAlerts=$false;$book=$excel.Workbooks.Add();$excel.Visible=$true
   $hwnd=[long]$excel.Hwnd
   [LiveHenkan.Tests.Driver]::Move($hwnd,120,120,1000,640)
   if(![LiveHenkan.Tests.Driver]::Activate($hwnd)){throw 'Could not activate our Excel'}
   $excel.ActiveSheet.Range('A1').Select()|Out-Null
   Start-Sleep -Milliseconds 800
   # Read: every filled cell of column A joined, so each confirmed entry is seen in order.
   return New-TestApp 'excel' $new[0].Id $hwnd {param($a) $s='';for($i=1;$i -le 40;$i++){$v=$a.State.Excel.ActiveSheet.Cells.Item($i,1).Value2;if($null -eq $v){break};$s+=[string]$v};$s} {param($a) $a.State.Excel.ActiveSheet.Range('A1:A40').ClearContents()|Out-Null;$a.State.Excel.ActiveSheet.Range('A1').Select()|Out-Null;Start-Sleep -Milliseconds 150} {
    param($a)
    try{$a.State.Book.Close($false)}catch{}
    try{$a.State.Excel.Quit()}catch{}
    try{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($a.State.Book);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($a.State.Excel)}catch{}
    Start-Sleep -Milliseconds 800
    $p=Get-Process -Id $a.Pid -ErrorAction SilentlyContinue;if($p -and !$p.HasExited){Stop-Process -Id $a.Pid -Force -ErrorAction SilentlyContinue}
   } ([pscustomobject]@{Excel=$excel;Book=$book})
  }
  'edge'{
   $edge=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe').'(default)'
   $profile=Join-Path $script:Results 'edge-profile'
   if(@(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'"|Where-Object{$_.CommandLine -like ('*'+$profile+'*')}).Count -gt 0){throw 'The test Edge profile is already in use'}
   $url=([uri](Join-Path $PSScriptRoot 'page.html')).AbsoluteUri
   $p=Start-Process -FilePath $edge -ArgumentList @(('--user-data-dir="'+$profile+'"'),'--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync','--disable-extensions','--new-window',$url) -PassThru
   $watch=[Diagnostics.Stopwatch]::StartNew();$hwnd=0
   while($watch.ElapsedMilliseconds -lt 15000 -and $hwnd -eq 0){Start-Sleep -Milliseconds 200;$p.Refresh();$hwnd=$p.MainWindowHandle.ToInt64()}
   if($hwnd -eq 0){throw 'Edge window not found'}
   [LiveHenkan.Tests.Driver]::Move($hwnd,120,120,1000,700)
   if(![LiveHenkan.Tests.Driver]::Activate($hwnd)){throw 'Could not activate our Edge'}
   Start-Sleep -Milliseconds 1500
   return New-TestApp 'edge' $p.Id $hwnd {
    param($a)
    $b=New-Object Text.StringBuilder 4096;[void][LiveHenkan.Native]::GetWindowText([IntPtr]$a.Hwnd,$b,4096);$t=$b.ToString()
    $i=$t.IndexOf('LH|');$j=$t.LastIndexOf('|END');if($i -lt 0 -or $j -lt 0){return $null}
    $body=$t.Substring($i+3,$j-$i-3);return $body.Substring($body.IndexOf('|')+1)
   } {param($a) [LiveHenkan.Tests.Driver]::OwnKey(65,$false,$true);[LiveHenkan.Tests.Driver]::OwnKey(8,$false,$false);Start-Sleep -Milliseconds 150} {
    param($a)
    $profile=Join-Path $script:Results 'edge-profile'
    [LiveHenkan.Tests.Driver]::Close($a.Hwnd);Start-Sleep -Milliseconds 1200
    foreach($proc in @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'"|Where-Object{$_.CommandLine -like ('*'+$profile+'*')})){Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue}
   } $null
  }
  default{throw "Unknown application: $Kind"}
 }
}
function Read-TestApp($app){return (& $app.Read $app)}
function Reset-TestApp($app){
 if([LiveHenkan.Tests.Driver]::Foreground() -ne $app.Hwnd){$null=[LiveHenkan.Tests.Driver]::Activate($app.Hwnd)}
 & $app.Reset $app
}
function Close-TestApp($app){if($app){try{& $app.Close $app}catch{}}}
