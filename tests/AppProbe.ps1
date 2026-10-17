param([string]$Kind='notepad')
# Prints what an application reports about its caret and text style, to tune the overlay.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
$D=[Henkan.Tests.Driver]
$target=$null
try{
 $target=Open-TestApp $Kind
 "app=$Kind pid=$($target.Pid) class="+[Henkan.Native]::ClassOf([IntPtr]$target.Hwnd)+" focus-class="+[Henkan.Native]::ClassOf([Henkan.Native]::FocusWindow())
 "empty: caret="+$D::Caret()+" style="+$D::Style()
 [Henkan.Native]::SendText(([string][char]0x4ECA+[char]0x65E5+[char]0x306F+'abc'),[Henkan.Native]::OwnMark)
 Start-Sleep -Milliseconds 600
 "typed: caret="+$D::Caret()+" style="+$D::Style()
 "text read back: ["+(Read-TestApp $target)+"]"
 $r=$D::Rect($target.Hwnd)
 $D::Capture($r[0],$r[1],$r[2]-$r[0],$r[3]-$r[1],(Join-Path $script:Results "probe\$Kind.png"))
 if($Kind -eq 'excel'){$D::OwnKey(27,$false,$false);Start-Sleep -Milliseconds 200}
}catch{"FAILED: "+($_|Out-String)}
finally{Close-TestApp $target}
