param([string]$ModelDir='',[string]$Root='')
# How the resident behaves around ordinary use: modes, mouse, shortcuts, candidates, a password box, emergency stop.
# Runs in a small window of the test's own (a classic edit box), through the real keyboard and mouse hooks.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
if(!('LiveHenkan.Tests.FormHost' -as [type])){
 $temp=Join-Path $script:Repo 'temp';[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp
 $null=Build-LiveHenkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'Driver.cs'),(Join-Path $PSScriptRoot 'FormHost.cs'))
 [LiveHenkan.Native]::PrepareProcess()
}
if(!$Root){$Root=$script:Repo}
$modelDir=if(Test-Path -LiteralPath (Join-Path $Root 'xsmall.jinen')){''}else{Get-TestModelDir $ModelDir}
$D=[LiveHenkan.Tests.Driver]
$userDir=Join-Path $script:Results 'behavior-user'
if(Test-Path -LiteralPath $userDir){Remove-Item -LiteralPath $userDir -Recurse -Force}
$checks=New-Object System.Collections.Generic.List[object]
function Check([string]$name,[bool]$ok,[string]$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=$ok;Detail=$detail});("{0} {1} {2}" -f $(if($ok){'ok  '}else{'FAIL'}),$name,$detail)}
function Idle([int]$ms=350){$null=Wait-Resident {param($s) $s['composing'] -eq 'False'} 3000;Start-Sleep -Milliseconds $ms}
function Converted([int]$timeoutMs=3000){
 $watch=[Diagnostics.Stopwatch]::StartNew();$last='';$since=0
 while($watch.ElapsedMilliseconds -lt $timeoutMs){
  $text=(Get-ResidentState)['text']
  if($text -eq $last -and $text.Length -gt 0 -and $text -cnotmatch '[a-z]'){if($watch.ElapsedMilliseconds-$since -ge 180){return $text}}
  else{$last=$text;$since=$watch.ElapsedMilliseconds}
  Start-Sleep -Milliseconds 20
 }
 return (Get-ResidentState)['text']
}
function Fresh{$null=Send-Resident 'reset';$form.Clear();$form.FocusPlain();if($D::Foreground() -ne $form.Hwnd){$null=$D::Activate($form.Hwnd)};Start-Sleep -Milliseconds 150}
$resident=$null;$form=$null
try{
 $resident=Start-Resident -Root $Root -ModelDir $modelDir -UserDir $userDir
 $form=New-Object LiveHenkan.Tests.FormHost
 $null=$D::Activate($form.Hwnd);$form.FocusPlain();Start-Sleep -Milliseconds 400

 Fresh
 $D::Type('tesuto',60);$shown=Converted
 $caret=(Get-ResidentState)['caret']
 Check 'caret of a classic edit box is found' ($caret -like 'system:*') $caret
 Check 'nothing is in the box before commit' ($form.Plain -eq '')
 $D::Key(13);Idle
 Check 'enter puts the text into the box' ($form.Plain -ceq $shown -and $shown.Length -gt 0) $form.Plain

 Fresh
 $D::Key(0x1D);Start-Sleep -Milliseconds 200
 $D::Type('abc 123',40);Start-Sleep -Milliseconds 200
 Check 'alphanumeric mode leaves the keys alone' ($form.Plain -ceq 'abc 123') $form.Plain
 $D::Key(0x1C);Start-Sleep -Milliseconds 200
 Check 'convert key returns to kana mode' ((Get-ResidentState)['mode'] -eq 'Kana')
 $D::Key(0xF3);Start-Sleep -Milliseconds 200
 Check 'hankaku/zenkaku key toggles' ((Get-ResidentState)['mode'] -eq 'Alnum')
 $D::Key(0xF4);Start-Sleep -Milliseconds 200
 Check 'and toggles back' ((Get-ResidentState)['mode'] -eq 'Kana')

 Fresh
 $D::Key(32);Start-Sleep -Milliseconds 150;$D::Key(32,$true,$false,$false);Start-Sleep -Milliseconds 150
 Check 'space is full-width, shift+space half-width' ($form.Plain -ceq ([string][char]0x3000+' ')) ([int[]][char[]]$form.Plain -join ',')
 $D::Key(8);$D::Key(8);Start-Sleep -Milliseconds 150
 Check 'backspace with nothing typed reaches the box' ($form.Plain -eq '')

 Fresh
 $D::Key(0x1D);Start-Sleep -Milliseconds 150;$D::Type('ABC',40);$D::Key(0x1C);Start-Sleep -Milliseconds 150
 $D::Type('tesuto',60);$shown=Converted
 $p=$form.PlainPoint(3,12)
 $D::Click($p[0],$p[1]);Start-Sleep -Milliseconds 500
 Check 'a click commits the text where it was being typed' ($form.Plain -ceq ('ABC'+$shown)) $form.Plain
 $D::Key(0x1D);Start-Sleep -Milliseconds 150;$D::Type('x',40);Start-Sleep -Milliseconds 200;$D::Key(0x1C);Start-Sleep -Milliseconds 150
 Check 'and the click itself still lands' ($form.Plain -ceq ('xABC'+$shown)) $form.Plain

 Fresh
 $D::Type('kaigi',60);$shown=Converted
 $D::Key(36);Start-Sleep -Milliseconds 300
 $D::Key(0x1D);Start-Sleep -Milliseconds 150;$D::Type('x',40);Start-Sleep -Milliseconds 200;$D::Key(0x1C);Start-Sleep -Milliseconds 150
 Check 'a key that moves the caret commits first, then acts (Home, then x)' ($form.Plain -ceq ('x'+$shown)) $form.Plain
 $D::Type('kaigi',60);$again=Converted
 $D::Key(35,$true,$false,$false);Start-Sleep -Milliseconds 300
 $D::Key(0x1D);Start-Sleep -Milliseconds 150;$D::Type('z',40);Start-Sleep -Milliseconds 200;$D::Key(0x1C);Start-Sleep -Milliseconds 150
 Check 'a key combination commits first, then acts (Shift+End selects the rest, z replaces it)' ($form.Plain -ceq ('x'+$again+'z')) $form.Plain
 Fresh
 $D::Type('kaigi',60);$third=Converted
 $D::Key(37,$false,$true,$false);Start-Sleep -Milliseconds 300
 $D::Key(0x1D);Start-Sleep -Milliseconds 150;$D::Type('y',40);Start-Sleep -Milliseconds 200;$D::Key(0x1C);Start-Sleep -Milliseconds 150
 Check 'a Ctrl shortcut commits first, then acts (Ctrl+Left moves back, then y lands inside the text)' ($form.Plain.Replace('y','') -ceq $third -and $form.Plain.Contains('y') -and !$form.Plain.EndsWith('y')) $form.Plain

 Fresh
 $D::Type('kaigi',60);$held=Converted
 [LiveHenkan.Native]::SendKeyDown(0xA4,[LiveHenkan.Native]::TestMark);Start-Sleep -Milliseconds 120
 $duringAlt=$form.Plain
 [LiveHenkan.Native]::SendKeyUp(0xA4,[LiveHenkan.Native]::TestMark);Start-Sleep -Milliseconds 150
 $D::Key(27);Start-Sleep -Milliseconds 200
 $D::Key(0x1D);Start-Sleep -Milliseconds 150;$D::Type('x',40);Start-Sleep -Milliseconds 200;$D::Key(0x1C);Start-Sleep -Milliseconds 150
 Check 'pressing Alt commits the text first and Alt is not left held' ($duringAlt -ceq $held -and $form.Plain -ceq ($held+'x')) "$duringAlt / $($form.Plain)"

 Fresh
 $D::Type('kikann',60);$first=Converted
 $D::Key(32);Start-Sleep -Milliseconds 900
 $second=(Get-ResidentState)['text']
 $D::Key(32);Start-Sleep -Milliseconds 300
 $third=(Get-ResidentState)['text']
 Check 'space steps through candidates' ($second -cne $first -and $third -cne $second -and $third.Length -gt 0) "$first / $second / $third"
 $D::Key(13);Idle
 Check 'the chosen candidate is committed' ($form.Plain -ceq $third) $form.Plain

 Fresh
 $D::Type('kikann',60);$null=Converted
 $D::Key(32);Start-Sleep -Milliseconds 900
 $D::Type('4',40);Start-Sleep -Milliseconds 300
 $picked=(Get-ResidentState)['text']
 $D::Key(13);Idle
 Check 'a number key picks a candidate' ($form.Plain -ceq $picked -and $picked.Length -gt 0) $picked

 Fresh
 $D::Type('kyouhaiitenkidesune',60);$all=Converted
 $D::Key(37);$D::Key(37);Start-Sleep -Milliseconds 200
 $D::Key(32);Start-Sleep -Milliseconds 900
 $changed=(Get-ResidentState)['text']
 $D::Key(13);Idle
 Check 'an earlier stretch can be chosen again' ($changed -cne $all -and $form.Plain -ceq $changed -and $changed.EndsWith($all.Substring($all.Length-5))) "$all -> $changed box=[$($form.Plain)]"

 Fresh
 $D::Type('tesuto',60);$null=Converted
 $D::Key(0x76);Start-Sleep -Milliseconds 200
 $kata=(Get-ResidentState)['text']
 $D::Key(0x79);Start-Sleep -Milliseconds 200
 $latin=(Get-ResidentState)['text']
 $D::Key(13);Idle
 Check 'F7 and F10 give katakana and the typed letters' ($kata.Length -eq 3 -and $latin -ceq 'tesuto' -and $form.Plain -ceq 'tesuto') "$kata / $latin"

 Fresh
 $D::Type('korehatesutodesu.tsuginobunmonyuuryokushimasu.',30);$null=Converted;$D::Key(13);Idle
 $burst=$form.Plain
 Check 'fast typing of two sentences loses nothing' ($burst.Length -ge 18 -and $burst -cnotmatch '[a-z]' -and $burst.Split([char]0x3002).Count -eq 3) $burst

 Fresh
 $D::Type('nagaibunwo',60);$null=Converted
 $null=Send-Resident 'settings';Start-Sleep -Milliseconds 900
 $state=Get-ResidentState
 Check 'when another window comes to the front the unfinished text is dropped, not typed elsewhere' ($state['composing'] -eq 'False' -and $form.Plain -eq '') $form.Plain
 $panel=$D::Panel($resident.Process.Id);if($panel -ne 0){$D::Close($panel);Start-Sleep -Milliseconds 500}
 $null=$D::Activate($form.Hwnd)

 Fresh
 $form.FocusSecret();Start-Sleep -Milliseconds 700
 $state=Get-ResidentState
 $D::Type('himitsu',50);Start-Sleep -Milliseconds 300
 Check 'a password box gets the keys as typed' ($form.Secret -ceq 'himitsu' -and $state['secure'] -eq 'True') ("secure="+$state['secure']+" len="+$form.Secret.Length)
 $form.FocusPlain();Start-Sleep -Milliseconds 700
 $D::Type('modoru',60);$back=Converted;$D::Key(13);Idle
 Check 'and conversion resumes outside it' ($form.Plain -ceq $back -and $back -cnotmatch '[a-z]') $form.Plain

 $metrics=Get-ResidentMetrics
 $state=Get-ResidentState
 Check 'no errors and no hook reinstall during the run' ($state['errors'] -eq '0' -and $state['rehooks'] -eq '0') ("errors="+$state['errors']+" rehooks="+$state['rehooks'])
 Check 'the keyboard hook always answered quickly' ([double]::Parse($state['hookMaxMs'],[Globalization.CultureInfo]::InvariantCulture) -lt 100) ("slowest "+$state['hookMaxMs']+" ms over "+$state['keys']+" key events")

 Fresh
 $D::Type('tochuu',60);$null=Converted
 $D::Key(0x7B,$true,$true,$false)
 $watch=[Diagnostics.Stopwatch]::StartNew();while($watch.ElapsedMilliseconds -lt 5000 -and !$resident.Process.HasExited){Start-Sleep -Milliseconds 50}
 Check 'Ctrl+Shift+F12 ends the resident' $resident.Process.HasExited ("after "+$watch.ElapsedMilliseconds+" ms")
 $form.Clear();$form.FocusPlain();Start-Sleep -Milliseconds 200
 $D::Type('abc',40);Start-Sleep -Milliseconds 200
 Check 'afterwards the keyboard is back to normal' ($form.Plain -ceq 'abc') $form.Plain
}catch{Check 'unexpected error' $false ($_|Out-String)}
finally{
 if($form){$form.Dispose()}
 "resident: "+(Stop-Resident $resident)
}
$failed=@($checks|Where-Object{!$_.Ok}).Count
[ordered]@{Checks=$checks;Failed=$failed;At=(Get-Date).ToString('s')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 (Join-Path $script:Results 'behavior-tests.json')
"behavior tests: $($checks.Count-$failed)/$($checks.Count)"
if($failed){exit 1}
