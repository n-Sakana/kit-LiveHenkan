param([string]$ModelDir='',[ValidateSet('notepad','word','excel','edge')][string]$Kind='notepad')
# Self-created application only; no clipboard, installation or user documents.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
$D=[Henkan.Tests.Driver]
$checks=New-Object System.Collections.Generic.List[object]
function Check([string]$name,[bool]$ok,[string]$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=$ok;Detail=$detail});("{0} {1} {2}" -f $(if($ok){'ok  '}else{'FAIL'}),$name,$detail)}
function Front {if($D::Foreground() -ne $app.Hwnd){throw 'Test window lost focus; input stopped.'}}
function Send-MixedText([string]$text){Front;$D::Type($text,25)}
function Key([int]$vk,[bool]$shift=$false,[bool]$ctrl=$false){Front;$D::Key($vk,$shift,$ctrl,$false)}
function Settled([string]$expected){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{$s=Get-ResidentState;if($s['text'] -ceq $expected){Start-Sleep -Milliseconds 200;return (Get-ResidentState)};Start-Sleep -Milliseconds 30}while($watch.ElapsedMilliseconds -lt 3000)
 return $s
}
function Commit {Key 13;$null=Wait-Resident {param($s) $s['composing'] -eq 'False'} 3000;Start-Sleep -Milliseconds 500;if($Kind -eq 'excel'){Key 13;Start-Sleep -Milliseconds 350}}
function Fresh {$null=Send-Resident 'reset';Front;Reset-TestApp $app;Start-Sleep -Milliseconds 150}
$resident=$null;$app=$null;$metrics=$null;$state=$null
$userDir=Join-Path $script:Results ('mixed-surface-user-'+$Kind)
if(Test-Path -LiteralPath $userDir){Remove-Item -LiteralPath $userDir -Recurse -Force}
try {
 $app=Open-TestApp $Kind
 $D::TargetWindow=$app.Hwnd
 $resident=Start-Resident -Root $script:Repo -ModelDir (Get-TestModelDir $ModelDir) -UserDir $userDir
 Start-Sleep -Milliseconds 1800
 $sent=$D::KeysSent;$seen=[int](Get-ResidentState)['testKeys']
 $lines=[IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'mixed.tsv'),[Text.Encoding]::UTF8)
 foreach($index in @(0,7,16,17)){
  Fresh;$p=$lines[$index].Split([char]9);Send-MixedText $p[0];$s=Settled $p[1]
  Check ('shown '+$p[0]) ($s['text'] -ceq $p[1]) $s['text']
  Check ('surface '+$p[0]) ($s['surface'] -eq $(if($Kind -eq 'notepad'){'inline'}else{'overlay'})) $s['surface']
  if($Kind -eq 'notepad'){Check 'inline document equals view' ((Read-TestApp $app) -ceq $p[1]) (Read-TestApp $app)}
  else{Check 'overlay waits for confirmation' ((Read-TestApp $app) -eq '') (Read-TestApp $app)}
  Commit;Check ('committed '+$p[0]) ((Read-TestApp $app) -ceq $p[1]) (Read-TestApp $app)
 }
 Fresh;Send-MixedText 'google';$null=Settled 'google';Key 8;Send-MixedText 'e';$null=Settled 'google';Commit
 Check 'backspace and retype' ((Read-TestApp $app) -ceq 'google') (Read-TestApp $app)
 Fresh;Send-MixedText 'github';$null=Settled 'github';Key 27;Key 27;Start-Sleep -Milliseconds 400
 Check 'escape cancels without document changes' ((Read-TestApp $app) -eq '' -and (Get-ResidentState)['composing'] -eq 'False') (Read-TestApp $app)
 Fresh;Send-MixedText 'apiwohiraku';$null=Settled ([string][char]0x30A2+[char]0x30D4+[char]0x3092+[char]0x958B+[char]0x304F);Key 37;Key 0x79
 $expected='api'+[char]0x3092+[char]0x958B+[char]0x304F;$s=Settled $expected;Commit
 Check 'F10 changes only the word' ((Read-TestApp $app) -ceq $expected) (Read-TestApp $app)
 Fresh;Send-MixedText 'apiwohiraku';$s=Settled $expected;Commit
 Check 'next input uses learned language' ((Read-TestApp $app) -ceq $expected) (Read-TestApp $app)
 if($Kind -eq 'notepad'){
  Fresh;Send-MixedText 'github';$null=Settled 'github';Commit;Key 0x5A $false $true;Start-Sleep -Milliseconds 350
  Check 'one undo removes confirmed mixed text' ((Read-TestApp $app) -eq '') (Read-TestApp $app)
 }
 $metrics=Get-ResidentMetrics;$state=Get-ResidentState
 Check 'no lost tagged key presses' (($D::KeysSent-$sent) -eq ([int]$state['testKeys']-$seen)) ("sent="+($D::KeysSent-$sent)+" seen="+([int]$state['testKeys']-$seen))
 Check 'no resident errors' ($state['errors'] -eq '0') $state['errors']
} catch {Check 'unexpected error' $false ($_|Out-String)}
finally {
 $D::TargetWindow=0
 if($resident){"resident: "+(Stop-Resident $resident)}
 if($app){Close-TestApp $app}
}
$failed=@($checks|Where-Object{!$_.Ok}).Count
[ordered]@{Kind=$Kind;Checks=$checks;Failed=$failed;ShownP50=(Get-Percentile $metrics.Shown 0.5);ShownP95=(Get-Percentile $metrics.Shown 0.95);ConvertedP50=(Get-Percentile $metrics.Converted 0.5);ConvertedP95=(Get-Percentile $metrics.Converted 0.95);State=$state;At=(Get-Date).ToString('s')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 (Join-Path $script:Results ('mixed-surface-'+$Kind+'.json'))
"mixed surface ${Kind}: $($checks.Count-$failed)/$($checks.Count)"
if($failed){exit 1}
