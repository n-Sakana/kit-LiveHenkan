param([string]$ModelDir='',[string]$Root='')
# Opens the word window and the settings window of the resident, registers a word by typing, and checks the files.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
if(!$Root){$Root=$script:Repo}
$modelDir=if(Test-Path -LiteralPath (Join-Path $Root 'xsmall.jinen')){''}else{Get-TestModelDir $ModelDir}
$D=[LiveHenkan.Tests.Driver]
$userDir=Join-Path $script:Results 'window-user'
if(Test-Path -LiteralPath $userDir){Remove-Item -LiteralPath $userDir -Recurse -Force}
$checks=New-Object System.Collections.Generic.List[object]
function Check([string]$name,[bool]$ok,[string]$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=$ok;Detail=$detail});("{0} {1} {2}" -f $(if($ok){'ok  '}else{'FAIL'}),$name,$detail)}
function Wait-Panel([int]$processId,[bool]$present){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while($watch.ElapsedMilliseconds -lt 6000){$h=$D::Panel($processId);if(($h -ne 0) -eq $present){return $h};Start-Sleep -Milliseconds 100}
 return $D::Panel($processId)
}
function Shot($hwnd,[string]$name){
 Start-Sleep -Milliseconds 400
 # The window has a see-through margin for its shadow; that margin is left out so nothing behind it gets into the picture.
 $r=$D::Rect($hwnd);$inset=[int][math]::Ceiling(19*[LiveHenkan.Native]::ScaleAt($r[0]+40,$r[1]+40))+1
 $D::Capture($r[0]+$inset,$r[1]+$inset,$r[2]-$r[0]-2*$inset,$r[3]-$r[1]-2*$inset,(Join-Path $script:Results "windows\$name.png"))
}
$resident=$null
try{
 $resident=Start-Resident -Root $Root -ModelDir $modelDir -UserDir $userDir
 $processId=$resident.Process.Id
 $null=Send-Resident 'word'
 $panel=Wait-Panel $processId $true
 Check 'word window opens' ($panel -ne 0) ($D::Title($panel))
 Start-Sleep -Milliseconds 600
 Check 'word window is in front' ($D::Foreground() -eq $panel)
 Shot $panel 'words-1-empty'
 # reading box: kana must arrive as kana
 $D::Type('himawari',70);Start-Sleep -Milliseconds 400
 $state=Get-ResidentState
 $kanaOnly=($state['text'] -cnotmatch '[a-z]') -and $state['text'].Length -eq 4
 $D::Key(13);Start-Sleep -Milliseconds 300
 $D::Key(9);Start-Sleep -Milliseconds 200
 $D::Type('himawari',70);Start-Sleep -Milliseconds 500
 $converted=(Get-ResidentState)['text']
 Shot $panel 'words-2-typing'
 $D::Key(13);Start-Sleep -Milliseconds 300
 Shot $panel 'words-3-filled'
 $D::Key(13);Start-Sleep -Milliseconds 500
 Shot $panel 'words-4-registered'
 $file=Join-Path $userDir 'dictionary.tsv'
 $lines=@(if(Test-Path -LiteralPath $file){[IO.File]::ReadAllLines($file,[Text.Encoding]::UTF8)})
 Check 'reading box takes kana without conversion' $kanaOnly $state['text']
 Check 'word is saved to the user folder' ($lines.Count -eq 1 -and $lines[0].Split("`t")[0].Length -eq 4 -and $lines[0].Split("`t")[1] -eq $converted) ($lines -join ' | ')
 # a second word, then the list
 $D::Type('meado',70);$D::Key(13);Start-Sleep -Milliseconds 200;$D::Key(9);Start-Sleep -Milliseconds 200
 $windowsBefore=$D::Windows($processId).Count
 $D::Key(0x1D);Start-Sleep -Milliseconds 200
 $windowsAfter=$D::Windows($processId).Count
 Shot $panel 'words-5-alphanumeric'
 # The mode is shown by the mark in the title row. The indicator that floats at the caret would sit on the field below.
 Check 'switching the mode in the word window lays nothing over its fields' ($windowsAfter -eq $windowsBefore) ("windows of the resident: $windowsBefore before, $windowsAfter after")
 $D::Type('sample.text',40);Start-Sleep -Milliseconds 200
 $D::Key(13);Start-Sleep -Milliseconds 500
 $D::Key(0x1C);Start-Sleep -Milliseconds 200
 Shot $panel 'words-6-two'
 $lines=@([IO.File]::ReadAllLines($file,[Text.Encoding]::UTF8))
 Check 'second word typed in alphanumeric mode is saved' ($lines.Count -eq 2 -and $lines[1].Split("`t")[1] -eq 'sample.text') ($lines -join ' | ')
 $D::Key(27);$gone=Wait-Panel $processId $false
 Check 'escape closes the word window' ($gone -eq 0)
 $null=Send-Resident 'settings'
 $panel=Wait-Panel $processId $true
 Check 'settings window opens' ($panel -ne 0) ($D::Title($panel))
 Shot $panel 'settings-1'
 # toggle the first switch by clicking its row, then put it back
 $r=$D::Rect($panel)
 $before=[IO.File]::Exists((Join-Path $userDir 'settings.txt'))
 $D::Click($r[0]+120,$r[1]+18+46+24);Start-Sleep -Milliseconds 400
 Shot $panel 'settings-2-toggled'
 $saved=if(Test-Path -LiteralPath (Join-Path $userDir 'settings.txt')){[IO.File]::ReadAllText((Join-Path $userDir 'settings.txt'))}else{''}
 Check 'a changed setting is saved at once' ($saved -match 'ToggleWithHankaku=0') $saved.Replace("`r`n",' ')
 $D::Click($r[0]+120,$r[1]+18+46+24);Start-Sleep -Milliseconds 300
 $saved=[IO.File]::ReadAllText((Join-Path $userDir 'settings.txt'))
 Check 'and saved again when put back' ($saved -match 'ToggleWithHankaku=1')
 $D::Key(27);$gone=Wait-Panel $processId $false
 Check 'escape closes the settings window' ($gone -eq 0)
 $state=Get-ResidentState
 Check 'no errors in the resident' ($state['errors'] -eq '0') $state['errors']
}catch{Check 'unexpected error' $false ($_|Out-String)}
finally{"resident: "+(Stop-Resident $resident)}
$failed=@($checks|Where-Object{!$_.Ok}).Count
[ordered]@{Checks=$checks;Failed=$failed;At=(Get-Date).ToString('s')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 (Join-Path $script:Results 'window-tests.json')
"window tests: $($checks.Count-$failed)/$($checks.Count)"
if($failed){exit 1}
