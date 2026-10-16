param([ValidateSet('notepad','word','excel','edge')][string]$Kind='notepad',[int]$Count=10,[int]$IntervalMs=70,[string]$ModelDir='',[string]$Root='',[switch]$Shots)
# Types sentences into an application of our own through the real keyboard hook and checks what arrives.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
if(!$Root){$Root=$script:Repo}
$modelDir=if(Test-Path -LiteralPath (Join-Path $Root 'xsmall.jinen')){''}else{Get-TestModelDir $ModelDir}
$D=[LiveHenkan.Tests.Driver]
$all=Get-Content -LiteralPath (Join-Path $script:Repo 'corpus.json') -Raw -Encoding UTF8|ConvertFrom-Json
$corpus=@($all|Select-Object -First $Count)
$resident=$null;$target=$null;$rows=@();$failure='';$metrics=$null;$final=$null
# Polls until the shown text has no unfinished romaji and has stopped changing.
function Wait-Converted([int]$timeoutMs=4000){
 $watch=[Diagnostics.Stopwatch]::StartNew();$last='';$since=0
 while($watch.ElapsedMilliseconds -lt $timeoutMs){
  $state=Get-ResidentState;$text=$state['text']
  if($text -eq $last -and $text.Length -gt 0 -and $text -cnotmatch '[a-z]'){if($watch.ElapsedMilliseconds-$since -ge 160){return $state}}
  else{$last=$text;$since=$watch.ElapsedMilliseconds}
  Start-Sleep -Milliseconds 20
 }
 return (Get-ResidentState)
}
try{
 $resident=Start-Resident -Root $Root -ModelDir $modelDir
 $target=Open-TestApp $Kind
 foreach($r in $corpus){
  $null=Send-Resident 'reset'
  Reset-TestApp $target
  if($D::Foreground() -ne $target.Hwnd){throw 'Our window is not in front'}
  $immediate=($r.id % 2 -eq 1)
  $D::Type($r.romaji,$IntervalMs)
  $shown=$null
  if(!$immediate){
   $state=Wait-Converted;$shown=$state['text']
   if($Shots -and $r.id -le 2){$null=Save-WindowShot $target.Hwnd "apps\$Kind-$($r.id)-composing.png"}
  }
  $before=[int](Get-ResidentState)['commits']
  $D::Key(13)
  $state=Wait-Resident {param($s) $s['composing'] -eq 'False' -and [int]$s['commits'] -gt $before} 4000
  if($Kind -eq 'excel'){
   # The first Enter put the text into the cell editor; the second one confirms the cell.
   Start-Sleep -Milliseconds 300;$D::Key(13)
   $watch=[Diagnostics.Stopwatch]::StartNew()
   while($watch.ElapsedMilliseconds -lt 3000 -and [LiveHenkan.Native]::ClassOf([LiveHenkan.Native]::FocusWindow()) -ne 'EXCEL7'){Start-Sleep -Milliseconds 50}
  }
  Start-Sleep -Milliseconds 250
  $actual=Read-TestApp $target
  if($Shots -and $r.id -le 2){$null=Save-WindowShot $target.Hwnd "apps\$Kind-$($r.id)-committed.png"}
  $rows+=[pscustomobject]@{Id=$r.id;Romaji=$r.romaji;Expected=$r.expected;Shown=$shown;Actual=$actual;ImmediateEnter=$immediate;Match=($actual -ceq $r.expected);Caret=$state['caret']}
  "$Kind $($r.id) match=$($rows[-1].Match) [$actual]"
 }
 $metrics=Get-ResidentMetrics
 $final=Get-ResidentState
}catch{
 $failure=($_|Out-String);"FAILED: $failure"
 # What was on the screen when it went wrong: the window in front, and a picture of our own window only.
 try{
  $front=$D::Foreground()
  $failure+=" | in front: ["+$D::Title($front)+"] class "+[LiveHenkan.Native]::ClassOf([IntPtr]$front)+", focus class "+[LiveHenkan.Native]::ClassOf([LiveHenkan.Native]::FocusWindow())
  $failure+=" | resident: "+(Send-Resident 'state' 500)
  "FAILED (more): $failure"
  if($target){$null=Save-WindowShot $target.Hwnd "appsailure-$Kind.png"}
 }catch{}
}
finally{
 Close-TestApp $target
 $stopped=Stop-Resident $resident
}
$summary=[ordered]@{
 App=$Kind;Root=$Root;IntervalMs=$IntervalMs;Rows=$rows;Matched=@($rows|Where-Object{$_.Match}).Count;Total=$rows.Count
 KeyToShownMs=if($metrics){[ordered]@{N=$metrics.Shown.Count;Median=(Get-Percentile $metrics.Shown 0.5);P95=(Get-Percentile $metrics.Shown 0.95);Max=(Get-Percentile $metrics.Shown 1)}}else{$null}
 KeyToConvertedMs=if($metrics){[ordered]@{N=$metrics.Converted.Count;Median=(Get-Percentile $metrics.Converted 0.5);P95=(Get-Percentile $metrics.Converted 0.95);Max=(Get-Percentile $metrics.Converted 1)}}else{$null}
 Threads=if($metrics){$metrics.Threads}else{$null};Errors=if($final){$final['errors']}else{$null};Rehooks=if($final){$final['rehooks']}else{$null}
 KeysSent=$D::KeysSent;KeysSeenByHook=if($final){[int]$final['testKeys']}else{$null};HookSlowestMs=if($final){$final['hookMaxMs']}else{$null};HookLongestWaitMs=if($final){$final['hookWaitMaxMs']}else{$null}
 Resident=$stopped;Failure=$failure;At=(Get-Date).ToString('s')
}
$summary|ConvertTo-Json -Depth 6|Set-Content -Encoding UTF8 (Join-Path $script:Results "app-$Kind.json")
"{0}: {1}/{2} matched; key->shown median {3} p95 {4}; key->converted median {5} p95 {6}; keys sent {8} seen {9}; hook slowest {10} ms, longest wait {11} ms; resident {7}" -f $Kind,$summary.Matched,$summary.Total,$summary.KeyToShownMs.Median,$summary.KeyToShownMs.P95,$summary.KeyToConvertedMs.Median,$summary.KeyToConvertedMs.P95,$stopped,$summary.KeysSent,$summary.KeysSeenByHook,$summary.HookSlowestMs,$summary.HookLongestWaitMs
if($failure -or $summary.Matched -ne $summary.Total){exit 1}
