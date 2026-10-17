param([string]$ModelDir='',[string]$Root='')
# Text written into the document itself (Notepad and rich edit boxes): what is seen while typing, and that the
# document is never left holding anything the user did not confirm. Runs in a Notepad and a small window of the
# test's own, through the real keyboard and mouse hooks.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
if(!('Henkan.Tests.FormHost' -as [type])){
 $temp=Join-Path $script:Repo 'temp';[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp
 $null=Build-Henkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'Driver.cs'),(Join-Path $PSScriptRoot 'FormHost.cs'))
 [Henkan.Native]::PrepareProcess()
}
if(!$Root){$Root=$script:Repo}
$modelDir=if(Test-Path -LiteralPath (Join-Path $Root 'xsmall.jinen')){''}else{Get-TestModelDir $ModelDir}
$D=[Henkan.Tests.Driver]
$userDir=Join-Path $script:Results 'inline-user'
if(Test-Path -LiteralPath $userDir){Remove-Item -LiteralPath $userDir -Recurse -Force}
$shots=Join-Path $script:Results 'inline';[IO.Directory]::CreateDirectory($shots)|Out-Null
$checks=New-Object System.Collections.Generic.List[object]
function Check([string]$name,[bool]$ok,[string]$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=$ok;Detail=$detail});("{0} {1} {2}" -f $(if($ok){'ok  '}else{'FAIL'}),$name,$detail)}
function Idle([int]$ms=350){$null=Wait-Resident {param($s) $s['composing'] -eq 'False'} 3000;Start-Sleep -Milliseconds $ms}
function Converted([int]$timeoutMs=3000){
 $watch=[Diagnostics.Stopwatch]::StartNew();$last='';$since=0
 while($watch.ElapsedMilliseconds -lt $timeoutMs){
  $text=(Get-ResidentState)['text']
  if($text -eq $last -and $text.Length -gt 0 -and $text -cnotmatch '[a-z]'){if($watch.ElapsedMilliseconds-$since -ge 220){return $text}}
  else{$last=$text;$since=$watch.ElapsedMilliseconds}
  Start-Sleep -Milliseconds 20
 }
 return (Get-ResidentState)['text']
}
function Doc{Read-TestNotepadLines $notepad}
function Own([int]$vk,[bool]$shift=$false,[bool]$ctrl=$false){$D::OwnKey($vk,$shift,$ctrl);Start-Sleep -Milliseconds 70}
function Put([string]$text){[Henkan.Native]::SendText($text,[Henkan.Native]::OwnMark);Start-Sleep -Milliseconds (150+12*$text.Length)}
# Our Notepad in front, holding these lines, the caret behind the last one.
function Fresh([string[]]$lines){
 $null=Send-Resident 'reset'
 Clear-TestNotepad $notepad
 for($i=0;$i -lt $lines.Count;$i++){if($i -gt 0){Own 13};Put $lines[$i]}
 Start-Sleep -Milliseconds 150
}
function Undo{$D::Key(0x5A,$false,$true,$false);Start-Sleep -Milliseconds 350}
function CaretBox{
 $c=$D::Caret();$i=$c.IndexOf(':')
 if($i -lt 0){return $null}
 return @($c.Substring($i+1).Split(',')|ForEach-Object{[int]$_})
}
# A picture of the top of our Notepad's text only.
function Shot([string]$name,[int]$height=150){
 $r=$D::Rect($notepad.Hwnd)
 $D::Capture($r[0]+10,$r[1]+96,[math]::Min(860,$r[2]-$r[0]-20),$height,(Join-Path $shots $name))
}
$maru=[string][char]0x3002
$resident=$null;$notepad=$null;$form=$null;$sent=0;$seen=0;$writes=$null
try{
 $resident=Start-Resident -Root $Root -ModelDir $modelDir -UserDir $userDir
 $form=New-Object Henkan.Tests.FormHost(1060,140)
 $notepad=Open-TestNotepad

 # ---- what is seen while typing
 Fresh
 $D::Type('kyouha',70);Start-Sleep -Milliseconds 250
 $s=Get-ResidentState
 Check 'Notepad takes the text into the document' ($s['surface'] -eq 'inline') $s['surface']
 Check 'the document shows what is being typed' ($s['text'].Length -gt 0 -and (Doc) -ceq $s['text']) (Doc)
 $D::Type('iitenkidesune',70);$shown=Converted
 Check 'and follows the conversion' ((Doc) -ceq $shown) (Doc)
 Shot '1-typing.png' 70
 $D::Key(13);Idle
 Check 'enter leaves exactly that text' ((Doc) -ceq $shown) (Doc)
 Undo
 Check 'one undo takes the whole confirmed text away' ((Doc) -eq '') (Doc)
 $D::Key(0x59,$false,$true,$false);Start-Sleep -Milliseconds 350
 Check 'and redo brings it back' ((Doc) -ceq $shown) (Doc)

 # ---- what was in the document already is not touched
 Fresh @('ABC','DEF','GHI')
 Own 0x24 $false $true;Own 0x28;Own 0x27
 $D::Type('tesuto',70);$shown=Converted
 $expect="ABC`nD"+$shown+"EF`nGHI"
 Check 'text goes in at the caret, between what was there' ((Doc) -ceq $expect) (Doc).Replace("`n",'/')
 $D::Key(13);Idle
 Check 'and stays there when confirmed' ((Doc) -ceq $expect) (Doc).Replace("`n",'/')
 $D::Type('nihongo',70);$null=Converted
 $D::Key(27);Start-Sleep -Milliseconds 200
 Check 'escape once keeps the text, as kana' ((Get-ResidentState)['composing'] -eq 'True' -and (Doc).Length -gt $expect.Length) (Doc).Replace("`n",'/')
 $D::Key(27);Idle
 Check 'escape twice takes the unconfirmed text out again' ((Doc) -ceq $expect) (Doc).Replace("`n",'/')
 $D::Type('ka',70);Start-Sleep -Milliseconds 250
 $with=Doc
 $D::Key(8);Idle
 Check 'backspace over the last character leaves the document as it was' ($with.Length -eq $expect.Length+1 -and (Doc) -ceq $expect) (Doc).Replace("`n",'/')
 Undo
 Check 'cancelled text left nothing to undo: undo removes the confirmed text' ((Doc) -ceq "ABC`nDEF`nGHI") (Doc).Replace("`n",'/')

 # ---- one confirmed stretch is one step of undo
 Fresh @('ABC')
 $D::Type('korehatesutodesu.tsugino',70);$view=Converted
 $doc=Doc
 Check 'a finished sentence is confirmed, the rest stays unconfirmed' ($doc.StartsWith('ABC') -and $doc.EndsWith($view) -and $doc.Contains($maru) -and !$view.Contains($maru)) $doc
 $D::Key(13);Idle;$full=Doc
 Undo;$one=Doc
 Check 'undo removes the last confirmed stretch only' ($one -ceq $full.Substring(0,$full.Length-$view.Length) -and $one.EndsWith($maru)) $one
 Undo
 Check 'and the next undo the sentence before it' ((Doc) -ceq 'ABC') (Doc)

 # ---- a selection is left to the application
 Fresh @('XYZ')
 Own 0x25 $true;Own 0x25 $true
 $D::Type('tesuto',70);$shown=Converted
 Check 'with a selection the text is shown over the document instead' ((Get-ResidentState)['surface'] -eq 'overlay' -and (Doc) -ceq 'XYZ') ((Get-ResidentState)['surface']+' '+(Doc))
 $D::Key(13);Idle
 Check 'and replaces the selection when confirmed, as typing does' ((Doc) -ceq ('X'+$shown)) (Doc)
 Undo
 Check 'which is one step of undo and brings the selected text back' ((Doc) -ceq 'XYZ') (Doc)

 # ---- choosing between conversions happens in the document too
 Fresh
 $D::Type('kyouhaiitenkidesu',70);$null=Converted
 $ok=$true;$trail='';$texts=New-Object System.Collections.Generic.HashSet[string]
 $n=0
 foreach($op in @(@(32,$false),@(32,$false),@(0x27,$false),@(0x25,$false),@(0x25,$true),@(0x27,$true),@(0x76,$false))){
  $D::Key($op[0],$op[1],$false,$false);Start-Sleep -Milliseconds 450
  $s=Get-ResidentState;$now=Doc;$n++
  if($now -cne $s['text'] -or $s['surface'] -ne 'inline'){$ok=$false;$trail+="[$n doc=$now view=$($s['text'])]"}
  $null=$texts.Add($now)
  if($n -eq 2){Shot '2-choosing.png' 330}
 }
 Check 'next candidate, phrase to the right and left, shorter, longer, katakana: all seen in the document' ($ok -and $texts.Count -ge 3) ("$($texts.Count) different texts "+$trail)
 $last=Doc
 $D::Key(13);Idle
 Check 'and enter confirms what was chosen' ((Doc) -ceq $last -and $last.Length -gt 0) (Doc)

 # ---- keys and clicks meant for the document find it finished
 Fresh
 $D::Type('tesuto',70);$shown=Converted
 $D::Key(0x1D);Start-Sleep -Milliseconds 300
 $D::Type('abc',50);Start-Sleep -Milliseconds 300
 Check 'switching to alphanumeric confirms the text; the next keys come after it' ((Doc) -ceq ($shown+'abc')) (Doc)
 $D::Key(0x1C);Start-Sleep -Milliseconds 250

 Fresh @('AB')
 Own 0x24
 $D::Type('tesuto',70);$shown=Converted
 $D::Key(0x23);Idle
 Put '!'
 Check 'a key that acts on the document (End) comes after the confirmed text' ((Doc) -ceq ($shown+'AB!')) (Doc)

 # Keys that go to Notepad itself, with the next text typed straight behind them.
 Fresh
 $D::Key(0x1D);Start-Sleep -Milliseconds 250
 $D::Type('abcdefghij',10);$D::Key(0x1C)
 $D::Type('tesuto',70);$shown=Converted
 Check 'letters typed just before are not overtaken' ((Doc) -ceq ('abcdefghij'+$shown)) (Doc)
 $D::Key(13);Idle
 Fresh @('AB')
 $D::Key(13);$D::Type('tesuto',70);$shown=Converted
 Check 'nor is a line break' ((Doc) -ceq ("AB`n"+$shown)) (Doc).Replace("`n",'/')
 $D::Key(13);Idle
 Fresh
 $D::Key(32);$D::Type('tesuto',70);$shown=Converted
 Check 'nor a space' ((Doc) -ceq ([string][char]0x3000+$shown)) (Doc)
 $D::Key(13);Idle

 Fresh @('first','second')
 $box=CaretBox
 if($box){
  Own 0x24 $false $true
  $D::Type('tesuto',70);$shown=Converted
  $D::Click($box[0]+2,$box[1]+[int]($box[2]/2));Idle
  Put '!'
  Check 'a click confirms the text and then moves the caret' ((Doc) -ceq ($shown+"first`nsecond!")) (Doc).Replace("`n",'/')
 }else{Check 'a click confirms the text and then moves the caret' $false 'caret of Notepad not found'}

 Fresh @('AB')
 $D::Type('tesuto',70);$shown=Converted
 $null=$D::Activate($form.Hwnd);Start-Sleep -Milliseconds 600
 Check 'when another window comes to the front the text stays, confirmed' ((Doc) -ceq ('AB'+$shown) -and (Get-ResidentState)['composing'] -eq 'False') (Doc)
 $null=$D::Activate($notepad.Hwnd);$notepad.Field.SetFocus();Start-Sleep -Milliseconds 300
 Undo
 Check 'and is one step of undo' ((Doc) -ceq 'AB') (Doc)

 # ---- nothing flashes
 Fresh
 Put 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'
 $box=CaretBox;$r=$D::Rect($notepad.Hwnd)
 if($box){
  $left=$r[0]+30;$top=$box[1]+1;$height=[math]::Max(6,$box[2]-6)
  Own 0x24 $true;Start-Sleep -Milliseconds 300
  $D::StartWatch($left,$top,560,$height);Start-Sleep -Milliseconds 500;$seenSelection=$D::StopWatch()
  Own 0x27;Fresh
  $D::StartWatch($left,$top,560,$height)
  $D::Type('kyouhaiitenkidesune.korehatesutodesu.tsuginobunmo',70);$null=Converted
  $D::Key(13);Idle
  $w=$D::StopWatch()
  Check 'the watch can see a real selection highlight' ($seenSelection[1] -gt 0) ("$($seenSelection[1]) of $($seenSelection[0]) frames")
  Check 'no selection highlight is seen at any moment while typing' ($w[1] -eq 0 -and $w[0] -gt 50) ("$($w[0]) frames, most blue pixels in one frame $($w[3]) of $(560*$height)")
  Check 'text never disappears for an instant' ($w[2] -eq 0) ("$($w[2]) frames")
 }else{Check 'no selection highlight is seen at any moment while typing' $false 'caret of Notepad not found'}

 # ---- rich edit boxes of other programs, the older and the newer kind
 foreach($newer in $false,$true){
  $kind=if($newer){'newer'}else{'older'}
  $form.UseRich($newer)
  $null=$D::Activate($form.Hwnd);$null=Send-Resident 'reset'
  $form.SetRich("ABC`nDEF",5,0);Start-Sleep -Milliseconds 300
  $D::Type('tesuto',70);$shown=Converted
  $expect="ABC`nD"+$shown+"EF"
  Check "the $kind rich edit box takes the text into the document" ((Get-ResidentState)['surface'] -eq 'inline' -and $form.Rich -ceq $expect) ($form.RichClass+' '+(Get-ResidentState)['surface']+' '+$form.Rich.Replace("`n",'/'))
  $D::Key(13);Idle
  Check "$kind box: confirmed there" ($form.Rich -ceq $expect) $form.Rich.Replace("`n",'/')
  $D::Type('nihongo',70);$null=Converted
  $D::Key(27);Start-Sleep -Milliseconds 200;$D::Key(27);Idle
  Check "$kind box: cancelled without a trace" ($form.Rich -ceq $expect) $form.Rich.Replace("`n",'/')
  $form.RichUndo();Start-Sleep -Milliseconds 200
  Check "$kind box: one undo removes the confirmed text and nothing else" ($form.Rich -ceq "ABC`nDEF" -and !$form.RichCanUndo) ($form.Rich.Replace("`n",'/')+' can undo more: '+$form.RichCanUndo)
  $null=Send-Resident 'reset'
  $form.SetRich('XYZ',1,2);Start-Sleep -Milliseconds 300
  $D::Type('tesuto',70);$shown=Converted
  Check "$kind box: a selection is left to the box" ((Get-ResidentState)['surface'] -eq 'overlay' -and $form.Rich -ceq 'XYZ') ((Get-ResidentState)['surface']+' '+$form.Rich)
  $D::Key(13);Idle
  Check "$kind box: and is replaced on confirming" ($form.Rich -ceq ('X'+$shown)) $form.Rich
  $form.SetRich('',0,0)
 }

 # ---- stopping
 $null=$D::Activate($notepad.Hwnd);$notepad.Field.SetFocus();Start-Sleep -Milliseconds 300
 Fresh @('AB')
 $state=Get-ResidentState;$metrics=Get-ResidentMetrics
 $sent=$D::KeysSent;$seen=[int]$state['testKeys']
 $writes=$state
 Check 'no errors and no hook reinstall during the run' ($state['errors'] -eq '0' -and $state['rehooks'] -eq '0') ("errors="+$state['errors']+" rehooks="+$state['rehooks'])
 Check 'every key sent was seen' ($sent -eq $seen) ("sent $sent seen $seen")
 Check 'the keyboard hook always answered quickly' ([double]::Parse($state['hookMaxMs'],[Globalization.CultureInfo]::InvariantCulture) -lt 100) ("slowest "+$state['hookMaxMs']+" ms over "+$state['keys']+" key events")
 $D::Type('tesuto',70);$shown=Converted
 $stopped=Stop-Resident $resident
 Check 'switching off while typing keeps the text, confirmed' ((Doc) -ceq ('AB'+$shown) -and $stopped -eq 'exited') ((Doc)+' resident '+$stopped)
 Own 0x5A $false $true;Start-Sleep -Milliseconds 300
 Check 'and it is one step of undo' ((Doc) -ceq 'AB') (Doc)

 $resident=Start-Resident -Root $Root -ModelDir $modelDir -UserDir $userDir
 $null=$D::Activate($notepad.Hwnd);$notepad.Field.SetFocus();Start-Sleep -Milliseconds 300
 Fresh @('AB')
 $D::Type('korehatesutodesu.tochuu',70);$view=Converted
 $before=Doc
 $D::Key(0x7B,$true,$true,$false)
 $watch=[Diagnostics.Stopwatch]::StartNew();while($watch.ElapsedMilliseconds -lt 5000 -and !$resident.Process.HasExited){Start-Sleep -Milliseconds 50}
 Start-Sleep -Milliseconds 300
 Check 'the emergency stop ends the resident' $resident.Process.HasExited ("after "+$watch.ElapsedMilliseconds+" ms")
 Check 'and takes the unconfirmed text out; confirmed text stays' ($before.EndsWith($view) -and $view.Length -gt 0 -and (Doc) -ceq $before.Substring(0,$before.Length-$view.Length)) ((Doc)+' (was '+$before+')')

 # ---- a long document, and text that runs over the end of a line
 Close-TestNotepad $notepad;$notepad=$null
 $long=(0..2999|ForEach-Object{'line {0:D4} the quick brown fox jumps over the lazy dog' -f $_}) -join "`r`n"
 $notepad=Open-TestNotepad -Content $long
 $resident=Start-Resident -Root $Root -ModelDir $modelDir -UserDir $userDir
 $null=$D::Activate($notepad.Hwnd);$notepad.Field.SetFocus();Start-Sleep -Milliseconds 300
 for($i=0;$i -lt 40;$i++){$D::OwnKey(0x22,$false,$false)};Start-Sleep -Milliseconds 300
 Own 0x27;Own 0x27;Own 0x27
 $before=Doc
 $D::Type('kyouhaiitenkidesune',70);$shown=Converted
 $after=Doc
 $at=0;while($at -lt $before.Length -and $before[$at] -eq $after[$at]){$at++}
 $intact=($after.Length -eq $before.Length+$shown.Length) -and ($after.Substring($at,$shown.Length) -ceq $shown) -and (($after.Substring(0,$at)+$after.Substring($at+$shown.Length)) -ceq $before)
 Check 'in a document of 3000 lines only the typed text is added' ($intact -and $at -gt 1000) ("at character $at of $($before.Length)")
 $D::Key(13);Idle
 Check 'and the same after confirming' ((Doc) -ceq $after) ''
 Undo
 Check 'undo there restores the document exactly' ((Doc) -ceq $before) ''
 $state=Get-ResidentState
 "long document: writes=$($state['inlineWrites']) average write $($state['inlineWriteMs']) ms, slowest $($state['inlineSlowestMs']) ms, marks $($state['inlineLocateMs']) ms"
 $longWrite=$state['inlineWriteMs']

 Own 0x23 $false $true;Own 13
 $filler=[string][char]0x3042*40
 Put $filler
 $watch=[Diagnostics.Stopwatch]::StartNew();while($watch.ElapsedMilliseconds -lt 3000 -and !(Doc).EndsWith($filler)){Start-Sleep -Milliseconds 50}
 $before=Doc
 $D::Type('kyouhaiitenkidesunetoiu',70);$shown=Converted
 Start-Sleep -Milliseconds 300
 $r=$D::Rect($notepad.Hwnd);$box=CaretBox
 if($box){$D::Capture($r[0]+10,[math]::Max($r[1]+96,$box[1]-60),[math]::Min(860,$r[2]-$r[0]-20),110,(Join-Path $shots '3-wrapped.png'))}
 $now=Doc
 Check 'text that runs over the end of the line is all there' ($now -ceq ($before+$shown)) ($now.Substring([math]::Max(0,$now.Length-30)))
 $D::Key(27);Start-Sleep -Milliseconds 200;$D::Key(27);Idle
 Check 'and is all taken out again when cancelled' ((Doc) -ceq $before) ''
 $state=Get-ResidentState
 Check 'no errors in the long document' ($state['errors'] -eq '0') ("errors="+$state['errors'])

 # ---- switched off in the settings
 $null=Stop-Resident $resident
 $offDir=Join-Path $script:Results 'inline-user-off';[IO.Directory]::CreateDirectory($offDir)|Out-Null
 [IO.File]::WriteAllText((Join-Path $offDir 'settings.txt'),"WriteIntoDocument=0`r`n")
 $resident=Start-Resident -Root $Root -ModelDir $modelDir -UserDir $offDir
 $null=$D::Activate($notepad.Hwnd);$notepad.Field.SetFocus();Start-Sleep -Milliseconds 300
 $before=Doc
 $D::Type('tesuto',70);$shown=Converted
 Check 'with the setting switched off, Notepad too gets the text laid over it' ((Get-ResidentState)['surface'] -eq 'overlay' -and (Doc) -ceq $before) (Get-ResidentState)['surface']
 $D::Key(27);Start-Sleep -Milliseconds 200;$D::Key(27);Idle
}catch{Check 'unexpected error' $false ($_|Out-String)}
finally{
 # Each step on its own: whatever went wrong above, nothing of the test stays open.
 try{$null=[Henkan.Tests.Driver]::StopWatch()}catch{}
 try{Close-TestNotepad $notepad}catch{}
 try{if($form){$form.Dispose()}}catch{}
 "resident: "+(Stop-Resident $resident)
 "leftover resident: "+($null -ne (Send-Resident 'ping' 200))
}
$failed=@($checks|Where-Object{!$_.Ok}).Count
$summary=[ordered]@{Checks=$checks;Failed=$failed;At=(Get-Date).ToString('s')}
if($writes){
 $summary['KeysSent']=$sent;$summary['KeysSeen']=$seen
 $summary['Writes']=[int]$writes['inlineWrites'];$summary['WriteMs']=$writes['inlineWriteMs'];$summary['SlowestWriteMs']=$writes['inlineSlowestMs'];$summary['MarksMs']=$writes['inlineLocateMs']
 $summary['LongDocumentWriteMs']=$longWrite
 $summary['ShownMedianMs']=Get-Percentile $metrics.Shown 0.5;$summary['ShownP95Ms']=Get-Percentile $metrics.Shown 0.95
 $summary['ConvertedMedianMs']=Get-Percentile $metrics.Converted 0.5;$summary['ConvertedP95Ms']=Get-Percentile $metrics.Converted 0.95
 "writes into the document: $($writes['inlineWrites']), average $($writes['inlineWriteMs']) ms, slowest $($writes['inlineSlowestMs']) ms; marks $($writes['inlineLocateMs']) ms"
 "key->shown ms: n=$($metrics.Shown.Count) median $(Get-Percentile $metrics.Shown 0.5) p95 $(Get-Percentile $metrics.Shown 0.95); key->converted: median $(Get-Percentile $metrics.Converted 0.5) p95 $(Get-Percentile $metrics.Converted 0.95)"
}
$summary|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 (Join-Path $script:Results 'inline-tests.json')
"inline tests: $($checks.Count-$failed)/$($checks.Count)"
if($failed){exit 1}
