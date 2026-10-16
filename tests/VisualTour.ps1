param([ValidateSet('notepad','word','excel','edge')][string]$Kind='notepad',[string]$ModelDir='',[string]$Root='',[string]$Out='tour')
# Walks through the visible states in an application of our own and saves a screenshot of each, for review by eye.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
if(!$Root){$Root=$script:Repo}
$modelDir=if(Test-Path -LiteralPath (Join-Path $Root 'xsmall.jinen')){''}else{Get-TestModelDir $ModelDir}
$D=[LiveHenkan.Tests.Driver]
$resident=$null;$target=$null;$step=0
$script:region=$null
function Shot([string]$label){
 $script:step++
 Start-Sleep -Milliseconds 350
 $state=Get-ResidentState
 if(!$script:region){
  # Pictures show the area around the caret inside our own window and nothing else: no title bar with an account
  # picture, and nothing of whatever else is on the screen.
  $r=$D::Rect($target.Hwnd);$left=$r[0]+10;$top=$r[1]+2;$right=$r[2]-10;$bottom=$r[3]-10
  if($state['caret'] -match ':(\-?\d+),(\-?\d+),(\d+)'){
   $x=[int]$matches[1]-150;$y=[int]$matches[2]-64
   $left=[math]::Max($left,$x);$top=[math]::Max($top,$y);$right=[math]::Min($right,$left+760);$bottom=[math]::Min($bottom,$top+470)
  }
  $script:region=@($left,$top,($right-$left),($bottom-$top))
 }
 $path=Join-Path $script:Results ("{0}\{1}-{2:00}-{3}.png" -f $Out,$Kind,$script:step,$label)
 $D::Capture($script:region[0],$script:region[1],$script:region[2],$script:region[3],$path)
 "{0:00} {1}: [{2}]" -f $script:step,$label,$state['text']
}
try{
 $resident=Start-Resident -Root $Root -ModelDir $modelDir
 $target=Open-TestApp $Kind
 $D::Move($target.Hwnd,140,40,980,760)
 Start-Sleep -Milliseconds 1500
 $D::Type('kyouhaiitenkidesune',80);Start-Sleep -Milliseconds 400
 Shot 'live'
 $D::Key(32);Start-Sleep -Milliseconds 700
 Shot 'candidates'
 $D::Key(32);Shot 'next-candidate'
 $D::Key(37);Shot 'focus-left'
 $D::Key(32);Start-Sleep -Milliseconds 700
 Shot 'candidates-of-earlier'
 $D::Key(27);Start-Sleep -Milliseconds 500
 Shot 'back-to-live'
 $D::Key(13);Start-Sleep -Milliseconds 300
 if($Kind -eq 'excel'){$D::Key(13);Start-Sleep -Milliseconds 400}
 Shot 'committed'
 $D::Type('kikann',80);Start-Sleep -Milliseconds 300
 $D::Key(32);Start-Sleep -Milliseconds 700
 Shot 'homophones'
 $D::Key(0x76);Shot 'katakana'
 $D::Key(27);Start-Sleep -Milliseconds 300;$D::Key(27);Start-Sleep -Milliseconds 200
 $D::Key(0x1D);Start-Sleep -Milliseconds 250
 Shot 'mode-alnum'
 $D::Key(0x1C);Start-Sleep -Milliseconds 250
 Shot 'mode-kana'
}catch{"FAILED: "+($_|Out-String)}
finally{
 if($Kind -eq 'excel'){try{$D::OwnKey(27,$false,$false)}catch{}}
 Close-TestApp $target
 "resident: "+(Stop-Resident $resident)
}
