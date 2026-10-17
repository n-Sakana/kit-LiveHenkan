param([Parameter(Mandatory=$true)][string]$Root,[ValidateSet('notepad','edge','word','excel')][string]$Kind='notepad',[Parameter(Mandatory=$true)][datetime]$Deadline)
# Requires an approved foreground slot. All input is guarded to our own window.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
$D=[Henkan.Tests.Driver];$resident=$null;$app=$null
$checks=New-Object System.Collections.Generic.List[object]
function Check([string]$name,[bool]$ok,[string]$detail){$checks.Add([pscustomobject]@{Name=$name;Ok=$ok;Detail=$detail});if(!$ok){"FAIL $name : $detail"}}
function Guard {if((Get-Date) -ge $Deadline){throw 'Approved slot expired'};if($D::Foreground() -ne $app.Hwnd){throw 'Our test window lost focus; stopped input'}}
function Key([int]$vk){Guard;$D::Key($vk)}
function Settled([string]$expected){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{Guard;$state=Get-ResidentState;if($state['text'] -ceq $expected){Start-Sleep -Milliseconds 220;return (Get-ResidentState)};Start-Sleep -Milliseconds 40}while($watch.ElapsedMilliseconds -lt 3500)
 return $state
}
try{
 if((Get-Date) -ge $Deadline){throw 'Approved slot expired'}
 $resident=Start-Resident -Root $Root -UserDir (Join-Path $script:Results ('format-surface-user-'+$Kind+'-'+[guid]::NewGuid().ToString('N')))
 $app=Open-TestApp $Kind;$D::TargetWindow=$app.Hwnd
 # Our new worksheet uses text cells, so Excel's own date/time coercion cannot
 # obscure the exact string delivered by the input tool.
 if($Kind -eq 'excel'){$app.State.Sheet.Range('A1:A40').NumberFormat='@'}
 $cases=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'formatting-surface.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $index=0
 foreach($case in $cases){
  $index++;Guard;$null=Send-Resident 'reset';Reset-TestApp $app;Guard
  $D::Type($case.keys,40);$s=Settled $case.shown
  Check "$index preview" ($s['text'] -ceq $case.shown) $s['text']
  Check "$index surface" ($s['surface'] -eq $(if($Kind -eq 'notepad'){'inline'}else{'overlay'})) $s['surface']
  $before=Read-TestApp $app
  Check "$index document before commit" ($before -ceq $(if($Kind -eq 'notepad'){$case.shown}else{''})) $before
  if($case.action -eq 'original'){Key 32;$s=Settled $case.expected;Check "$index restored" ($s['text'] -ceq $case.expected) $s['text']}
  if($case.action -eq 'backspace'){Key 8;$s=Settled $case.expected;Check "$index backspace" ($s['text'] -ceq $case.expected) $s['text']}
  if($case.action -eq 'cancel'){Key 27;Key 27}else{Key 13}
  $null=Wait-Resident {param($state) $state['composing'] -eq 'False'} 3500
  Start-Sleep -Milliseconds 300
  if($Kind -eq 'excel' -and $case.action -ne 'cancel'){Key 13;Start-Sleep -Milliseconds 500}
  $actual=Read-TestApp $app;Check "$index committed" ($actual -ceq $case.expected) $actual
 }
 $state=Get-ResidentState;Check 'no resident errors' ($state['errors'] -eq '0') $state['errors']
}catch{Check 'unexpected error' $false ($_|Out-String)}
finally{if($resident){$stopped=Stop-Resident $resident};if($app){Close-TestApp $app};$D::TargetWindow=0}
Check 'resident exits without forced stop' ($stopped -eq 'exited') ([string]$stopped)
$failed=@($checks|Where-Object {!$_.Ok}).Count
[ordered]@{Kind=$Kind;Root=$Root;Checks=$checks;Passed=($checks.Count-$failed);Total=$checks.Count;Resident=$stopped;At=(Get-Date).ToString('o')}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results ('formatting-surface-'+$Kind+'.json'))
"Formatting surface ${Kind}: $($checks.Count-$failed)/$($checks.Count); resident $stopped"
if($failed){exit 1}
