param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$ModelDir,[string]$Label='current',[switch]$SkipMeasures,[switch]$Shortcut)
# Real hooks, held switching keys, application shortcut, on/off CPU, and cleanup of only our own windows.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $Root 'app') -Extra @((Join-Path $PSScriptRoot 'Driver.cs'),(Join-Path $PSScriptRoot 'FormHost.cs'))
[Henkan.Native]::PrepareProcess()
$D=[Henkan.Tests.Driver];$N=[Henkan.Native]
$checks=New-Object System.Collections.Generic.List[object]
function Check($name,$ok,$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=[bool]$ok;Detail=$detail});"$ok $name $detail"}
$resident=$null;$form=$null;$measurements=@()
try {
 $form=New-Object Henkan.Tests.FormHost
 $null=$D::Activate($form.Hwnd);$D::TargetWindow=$form.Hwnd;$form.FocusPlain()
 if(!$SkipMeasures){
  $N::CloseSystemIme($N::FocusWindow());$p=[Diagnostics.Process]::GetCurrentProcess();$start=$p.TotalProcessorTime.TotalMilliseconds;$watch=[Diagnostics.Stopwatch]::StartNew()
  foreach($text in @('kaigi','shigoto','ashita','tenki')){$D::Type($text,170);Start-Sleep -Milliseconds 600;$D::Key(13);Start-Sleep -Milliseconds 150}
  $p.Refresh();$measurements += [pscustomobject]@{Mode='stopped';CpuMs=0;AppCpuMs=[math]::Round($p.TotalProcessorTime.TotalMilliseconds-$start);ElapsedMs=$watch.ElapsedMilliseconds;WorkingSetMB=0}
  $form.Clear()
 }
 $userDir=Join-Path $script:Results ('quality-user-'+$Label+'-'+(Get-Date -Format HHmmss))
 [IO.Directory]::CreateDirectory($userDir)|Out-Null
 if($Shortcut){'ToggleWithCtrlShiftSpace=1'|Set-Content -Encoding ASCII (Join-Path $userDir 'settings.txt')}
 $resident=Start-Resident -Root $Root -ModelDir $ModelDir -UserDir $userDir
 Start-Sleep -Milliseconds 2000
 $D::Key(0x1C);Start-Sleep -Milliseconds 100
 try {
  for($i=0;$i -lt 4;$i++){$N::SendKeyDown(0x19,$N::TestMark);Start-Sleep -Milliseconds 120}
  Check 'held mode key switches once' ((Get-ResidentState)['mode'] -eq 'Alnum')
 } finally {$N::SendKeyUp(0x19,$N::TestMark)}
 Start-Sleep -Milliseconds 100;$D::Key(0x19);Start-Sleep -Milliseconds 100
 Check 'next press switches back' ((Get-ResidentState)['mode'] -eq 'Kana')
 $D::Key(32,$true,$true,$false);Start-Sleep -Milliseconds 100
 Check 'Ctrl Shift Space follows its setting' ((Get-ResidentState)['mode'] -eq $(if($Shortcut){'Alnum'}else{'Kana'}))
 $D::Key(0x1C)
 foreach($mode in $(if($SkipMeasures){@()}else{@('alnum','kana','alnum','kana')})){
  $null=Send-Resident 'reset';$form.Clear();$form.FocusPlain();$null=Send-Resident $mode
  Start-Sleep -Milliseconds 1200
  $p=$resident.Process;$p.Refresh();$hostProc=[Diagnostics.Process]::GetCurrentProcess();$hostStart=$hostProc.TotalProcessorTime.TotalMilliseconds;$start=$p.TotalProcessorTime.TotalMilliseconds;$watch=[Diagnostics.Stopwatch]::StartNew()
  foreach($text in @('kaigi','shigoto','ashita','tenki')){$D::Type($text,170);Start-Sleep -Milliseconds 600;$D::Key(13);Start-Sleep -Milliseconds 150}
  $p.Refresh();$hostProc.Refresh();$measurements += [pscustomobject]@{Mode=$mode;CpuMs=[math]::Round($p.TotalProcessorTime.TotalMilliseconds-$start);AppCpuMs=[math]::Round($hostProc.TotalProcessorTime.TotalMilliseconds-$hostStart);ElapsedMs=$watch.ElapsedMilliseconds;WorkingSetMB=[math]::Round($p.WorkingSet64/1MB)}
 }
 $state=Get-ResidentState
 Check 'no runtime errors' ($state['errors'] -eq '0')
 Check 'mode still kana' ($state['mode'] -eq 'Kana')
 Check 'hook below 100ms' ([double]$state['hookMaxMs'] -lt 100) $state['hookMaxMs']
 if($Label -notlike 'baseline*'){
  for($i=0;$i -lt 25;$i++){$null=Send-Resident 'test-recoverable-error'}
  Start-Sleep -Milliseconds 900
  Check '25 recoverable faults do not stop input' ((Send-Resident 'ping') -eq 'ok' -and (Get-ResidentState)['mode'] -eq 'Kana')
 }
} finally {if($form){$form.Dispose()};$stopped=Stop-Resident $resident}
$result=[ordered]@{Label=$Label;Checks=$checks;Measurements=$measurements;State=$state;Resident=$stopped;At=(Get-Date).ToString('s')}
$result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $script:Results ('quality-'+$Label+'.json'))
$measurements | Format-Table
if(@($checks|Where-Object {!$_.Ok}).Count){exit 1}
