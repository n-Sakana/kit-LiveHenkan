param([string]$ModelDir='')
# Reads Word settings and compares delivery paths in a new document. Does not change settings or use the clipboard.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
$D=[LiveHenkan.Tests.Driver]
$rows=New-Object System.Collections.Generic.List[object]
$app=$null;$resident=$null;$options=$null
$userDir=Join-Path $script:Results 'word-case-probe-user'
if(Test-Path -LiteralPath $userDir){Remove-Item -LiteralPath $userDir -Recurse -Force}
$text='api'+[char]0x3092+[char]0x958B+[char]0x304F
function Front {if($D::Foreground() -ne $app.Hwnd){throw 'Test window lost focus; input stopped.'}}
function Record([string]$name,[string]$shown=''){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 foreach($delay in @(40,160,600)){
  Start-Sleep -Milliseconds $delay
  $rows.Add([pscustomobject]@{Name=$name;AfterMs=$watch.ElapsedMilliseconds;Shown=$shown;Document=(Read-TestApp $app)})
 }
}
function Blank {Front;if($resident){$null=Send-Resident 'reset'};Reset-TestApp $app;Start-Sleep -Milliseconds 150}
try {
 $app=Open-TestApp 'word';$D::TargetWindow=$app.Hwnd
 $word=$app.State.Word
 $options=[ordered]@{CorrectSentenceCaps=$word.AutoCorrect.CorrectSentenceCaps;CorrectInitialCaps=$word.AutoCorrect.CorrectInitialCaps;ReplaceText=$word.AutoCorrect.ReplaceText;LanguageId=$word.Selection.LanguageID}
 # Same unicode keystroke delivery as the product, with no resident and no F10.
 Blank;Front;[LiveHenkan.Native]::SendText($text,[LiveHenkan.Native]::OwnMark);Record 'unicode without resident'
 Blank;Front;$D::Type('api',25);[LiveHenkan.Native]::SendText($text.Substring(3),[LiveHenkan.Native]::OwnMark);Record 'ordinary ASCII followed by Japanese without resident'
 $resident=Start-Resident -Root $script:Repo -ModelDir (Get-TestModelDir $ModelDir) -UserDir $userDir
 Start-Sleep -Milliseconds 1800
 # Compare conversion before and after language learning.
 Blank;Front;$D::Type('apiwohiraku',25);$null=Wait-Resident {param($s) $s['text'].Length -gt 4} 3000;Start-Sleep -Milliseconds 300
 $D::Key(37);$D::Key(0x79);Start-Sleep -Milliseconds 250;$shown=(Get-ResidentState)['text'];$D::Key(13);Record 'F10 then commit' $shown
 Blank;Front;$D::Type('apiwohiraku',25);$null=Wait-Resident {param($s) $s['text'] -ceq $text} 3000;Start-Sleep -Milliseconds 200
 $shown=(Get-ResidentState)['text'];$D::Key(13);Record 'learned then commit' $shown
 Blank;Front;$D::Type('apiwohiraku',25);$null=Wait-Resident {param($s) $s['text'] -ceq $text} 3000;Start-Sleep -Milliseconds 200
 $shown=(Get-ResidentState)['text'];$D::Key(36);Record 'learned then Home commit' $shown
} catch {$rows.Add([pscustomobject]@{Name='error';Document=($_|Out-String)})}
finally {
 $D::TargetWindow=0
 if($resident){"resident: "+(Stop-Resident $resident)}
 if($app){Close-TestApp $app}
}
$result=[ordered]@{Expected=$text;WordOptions=$options;Rows=$rows;At=(Get-Date).ToString('s')}
$result|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results 'word-case-probe.json')
$result|ConvertTo-Json -Depth 5
