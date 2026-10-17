param([string]$ModelDir='',[string]$Root='')
# Quick end-to-end check in our own Notepad: start, type, look, commit, stop.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
if(!$Root){$Root=$script:Repo}
$modelDir=if(Test-Path -LiteralPath (Join-Path $Root 'xsmall.jinen')){''}else{Get-TestModelDir $ModelDir}
$D=[Henkan.Tests.Driver]
$resident=$null;$notepad=$null
function Zoom([string]$name){
 $r=$D::Rect($notepad.Hwnd)
 $path=Join-Path $script:Results $name
 $D::CaptureZoom($r[0]+14,$r[1]+96,330,44,3,$path)
 return $path
}
try{
 $resident=Start-Resident -Root $Root -ModelDir $modelDir
 "resident started in $($resident.StartMs) ms, pid $($resident.Process.Id)"
 $notepad=Open-TestNotepad
 "caret seen by probe: "+$D::Caret()
 $D::Type('kyouhaiitenkidesune',90)
 $state=Wait-Resident {param($s) $s['text'].Length -gt 0 -and $s['text'] -notmatch '[a-z]'} 2000
 "composing: ["+$state['text']+"] caret="+$state['caret']
 Start-Sleep -Milliseconds 300
 "shot: "+(Save-WindowShot $notepad.Hwnd 'smoke\1-composing.png')
 "zoom: "+(Zoom 'smoke\1-composing-zoom.png')
 "document while composing: ["+(Read-TestNotepad $notepad)+"]"
 $D::Key(13)
 $null=Wait-Resident {param($s) $s['composing'] -eq 'False'} 2000
 Start-Sleep -Milliseconds 300
 "document after Enter: ["+(Read-TestNotepad $notepad)+"]"
 "zoom: "+(Zoom 'smoke\1-committed-zoom.png')
 $D::Type('korehatesutodesu.tsugino',90)
 Start-Sleep -Milliseconds 600
 $state=Get-ResidentState
 "second: view=["+$state['text']+"] doc=["+(Read-TestNotepad $notepad)+"] caret="+$state['caret']
 "shot: "+(Save-WindowShot $notepad.Hwnd 'smoke\2-after-sentence.png')
 $D::Type("bunmo`n",90)
 $null=Wait-Resident {param($s) $s['composing'] -eq 'False'} 2000
 Start-Sleep -Milliseconds 300
 "final document: ["+(Read-TestNotepad $notepad)+"]"
 "shot: "+(Save-WindowShot $notepad.Hwnd 'smoke\3-final.png')
 $m=Get-ResidentMetrics
 "key->shown ms: n=$($m.Shown.Count) median $(Get-Percentile $m.Shown 0.5) p95 $(Get-Percentile $m.Shown 0.95) max $(Get-Percentile $m.Shown 1)"
 "key->converted shown ms: n=$($m.Converted.Count) median $(Get-Percentile $m.Converted 0.5) p95 $(Get-Percentile $m.Converted 0.95) max $(Get-Percentile $m.Converted 1)  threads=$($m.Threads)"
 $state=Get-ResidentState
 "state: mode=$($state['mode']) errors=$($state['errors']) rehooks=$($state['rehooks'])"
}catch{
 "FAILED: "+($_|Out-String)
}finally{
 Close-TestNotepad $notepad
 "resident: "+(Stop-Resident $resident)
 "leftover resident: "+($null -ne (Send-Resident 'ping' 200))
}
