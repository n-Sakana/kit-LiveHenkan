param([string]$Command='on',[string]$ModelDir='',[string]$UserDir='',[switch]$Test)
# Starts the resident, or hands the command to the one that is already running.
# This file stays ASCII: Windows PowerShell 5.1 reads a script without a byte order mark as ANSI.
$ErrorActionPreference='Stop'
$app=$PSScriptRoot
$root=Split-Path -Parent $app
. (Join-Path $app 'Common.ps1')

$reply=Send-Resident $Command
if($null -ne $reply){
 if($Command -in 'state','metrics','ping'){Write-Output $reply}
 exit 0
}
# Nothing is running: only a start request goes further.
if($Command -notin 'on','kana','toggle','word','settings'){exit 0}

try{
 if($PSVersionTable.PSVersion.Major -ne 5 -or ![Environment]::Is64BitProcess){throw 'Windows PowerShell 5.1 (64-bit) is required.'}
 $model=Resolve-Model -Root $root -ModelDir $ModelDir
 # The compiler's scratch files stay inside this folder when it is writable.
 $temp=Join-Path $root 'temp'
 try{[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp}catch{}
 $null=Build-LiveHenkan -App $app
 $code=[LiveHenkan.App]::Run([string]$root,[string]$model,[string]$Command,[bool]$Test,[string]$UserDir)
 exit [int]$code
}catch{
 $detail=($_|Out-String)
 try{
  $userDir=Join-Path $root 'user';[IO.Directory]::CreateDirectory($userDir)|Out-Null
  [IO.File]::WriteAllText((Join-Path $userDir 'error.txt'),$detail,[Text.Encoding]::UTF8)
 }catch{}
 if(!$Test){
  try{
   $s=Get-Strings
   Add-Type -AssemblyName System.Windows.Forms
   $inner=$_.Exception;while($inner.InnerException){$inner=$inner.InnerException}
   [void][System.Windows.Forms.MessageBox]::Show($s['start.failed']+"`r`n`r`n"+$inner.Message,$s['title'])
  }catch{}
 }else{Write-Output $detail}
 exit 1
}
