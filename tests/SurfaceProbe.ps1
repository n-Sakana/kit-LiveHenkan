# Why Word, Excel and the classic edit box keep the text laid over the document instead of written into it.
# Each part writes text from outside the way the resident would have to, in a window of the test's own, and shows
# what that does to the document's undo history. No resident runs. Results go to results\surface-probe.txt.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
if(!('LiveHenkan.Tests.FormHost' -as [type])){
 $temp=Join-Path $script:Repo 'temp';[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp
 $null=Build-LiveHenkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'Driver.cs'),(Join-Path $PSScriptRoot 'FormHost.cs'))
 [LiveHenkan.Native]::PrepareProcess()
}
$D=[LiveHenkan.Tests.Driver]
$lines=New-Object System.Collections.Generic.List[string]
function Say([string]$text){$lines.Add($text);$text}
$kana=[string][char]0x304B+[char]0x306A;$kanji=[string][char]0x4EEE+[char]0x540D+[char]0x3067

Say '===== classic edit box (class Edit)'
$form=$null
try{
 $form=New-Object LiveHenkan.Tests.FormHost
 $null=$D::Activate($form.Hwnd);$form.FocusPlain();Start-Sleep -Milliseconds 300
 [LiveHenkan.Native]::SendText('abc',[LiveHenkan.Native]::OwnMark);Start-Sleep -Milliseconds 300
 Say ("the user typed abc: text ["+$form.Plain+"] undo available="+$form.PlainCanUndo)
 $form.PlainInsert('X',$false)
 Say ("one insertion that is not to be recorded: text ["+$form.Plain+"] undo available="+$form.PlainCanUndo)
 $form.Clear();$form.FocusPlain()
 [LiveHenkan.Native]::SendText('abc',[LiveHenkan.Native]::OwnMark);Start-Sleep -Milliseconds 300
 $form.PlainInsert('X',$true);$form.PlainUndo()
 $afterOne=$form.Plain
 $form.PlainUndo()
 Say ("one recorded insertion, then undo: ["+$afterOne+"]; undo again: ["+$form.Plain+"] (the box keeps a single step, so the user's own step is gone)")
}catch{Say ("FAILED: "+($_|Out-String))}
finally{if($form){$form.Dispose()}}

Say '===== Word'
$target=$null
try{
 $target=Open-TestApp 'word'
 $w=$target.State.Word;$d=$target.State.Doc
 Say ("version "+$w.Version)
 $d.Content.InsertAfter('ABC')
 $u=$w.UndoRecord
 $u.StartCustomRecord('LiveHenkan')
 $sw=[Diagnostics.Stopwatch]::StartNew()
 $d.Range(3,3).Text=$kana;$t1=$sw.Elapsed.TotalMilliseconds;$sw.Restart()
 $d.Range(3,5).Text=$kanji;$t2=$sw.Elapsed.TotalMilliseconds
 $u.EndCustomRecord()
 Say ("two writes inside one custom undo record took {0:F1} and {1:F1} ms" -f $t1,$t2)
 $null=$d.Undo(1)
 Say ("after Undo(1): ["+$d.Content.Text.TrimEnd()+"] (one step for both, as wanted)")
 $d.Content.Text='ABC'
 $d.Range(3,3).Text=$kana;$d.Range(3,5).Text=$kanji
 $null=$d.Undo(1)
 Say ("the same two writes without a record, after Undo(1): ["+$d.Content.Text.TrimEnd()+"] (every write is a step of its own)")
 # A client that ends while its record is open, as the resident would if it were killed while text is being typed.
 $d.Content.Text='ABC'
 $child=@'
$w=[Runtime.InteropServices.Marshal]::GetActiveObject('Word.Application')
$w.UndoRecord.StartCustomRecord('LiveHenkan')
$w.ActiveDocument.Range(3,3).Text='zz'
[Diagnostics.Process]::GetCurrentProcess().Kill()
'@
 $p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-Command',$child) -PassThru -WindowStyle Hidden
 $null=$p.WaitForExit(20000);Start-Sleep -Milliseconds 800
 Say ("a client died with its record open: text ["+$d.Content.Text.TrimEnd()+"] still recording="+$w.UndoRecord.IsRecordingCustomRecord)
 $d.Range(0,0).InsertBefore('Q');$d.Range(0,0).InsertBefore('R')
 $null=$d.Undo(1)
 Say ("two later edits by the user, then Undo(1): ["+$d.Content.Text.TrimEnd()+"] still recording="+$w.UndoRecord.IsRecordingCustomRecord+" (everything since is undone as one step)")
 try{$w.UndoRecord.EndCustomRecord()}catch{}
 $d.Content.Text='ABC';$d.TrackRevisions=$true
 $d.Range(3,3).Text=$kana;$d.Range(3,5).Text=$kanji
 Say ("with change tracking on, the same two writes leave revisions: "+$d.Revisions.Count)
 $d.TrackRevisions=$false
}catch{Say ("FAILED: "+($_|Out-String))}
finally{Close-TestApp $target}

Say '===== Excel'
$target=$null
try{
 $target=Open-TestApp 'excel'
 $x=$target.State.Excel
 Say ("version "+$x.Version)
 [LiveHenkan.Native]::SendText('abc',[LiveHenkan.Native]::OwnMark);Start-Sleep -Milliseconds 400
 [LiveHenkan.Native]::SendKey(13,$false,$false,$false,[LiveHenkan.Native]::OwnMark);Start-Sleep -Milliseconds 700
 Say ("the user typed abc into A1: A1=["+$x.ActiveSheet.Range('A1').Value2+"] undo available="+$x.CommandBars.FindControl([Type]::Missing,128).Enabled)
 $x.ActiveSheet.Range('C1').Value2='x'
 Start-Sleep -Milliseconds 400
 Say ("after one write through automation: undo available="+$x.CommandBars.FindControl([Type]::Missing,128).Enabled+" (the user's undo history is gone)")
 [LiveHenkan.Native]::SendText('de',[LiveHenkan.Native]::OwnMark);Start-Sleep -Milliseconds 400
 try{$x.ActiveSheet.Range('D1').Value2='y';Say 'a write while a cell is being edited: accepted'}catch{Say 'a write while a cell is being edited: refused'}
 [LiveHenkan.Native]::SendKey(27,$false,$false,$false,[LiveHenkan.Native]::OwnMark);Start-Sleep -Milliseconds 400
}catch{Say ("FAILED: "+($_|Out-String))}
finally{Close-TestApp $target}
$lines.Add('at '+(Get-Date).ToString('s'))
[IO.File]::WriteAllLines((Join-Path $script:Results 'surface-probe.txt'),$lines,(New-Object Text.UTF8Encoding($false)))
