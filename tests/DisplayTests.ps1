param([string]$ModelDir,[string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$Label='current')
# One self-created local Edge page; no submission, network, user documents or registration.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
$D=[LiveHenkan.Tests.Driver]
$checks=New-Object System.Collections.Generic.List[object]
function Check($name,$ok,$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=[bool]$ok;Detail=$detail});"$ok $name $detail"}
function Display {
 $map=@{};foreach($part in (Send-Resident 'display').Split("`t")){$i=$part.IndexOf('=');if($i -gt 0){$map[$part.Substring(0,$i)]=$part.Substring($i+1)}};return $map
}
$resident=$null;$app=$null
try {
 $resident=Start-Resident -Root $Root -ModelDir $ModelDir -UserDir (Join-Path $script:Results ('display-user-'+$Label+'-'+(Get-Date -Format HHmmss)))
 $app=Open-TestApp 'edge';$D::TargetWindow=$app.Hwnd
 Start-Sleep -Milliseconds 1500
 foreach($index in 0,1,2){
  # Tab moves between textarea, input and contenteditable, all in the same HWND.
  if($index -gt 0){$D::Key(9);Start-Sleep -Milliseconds 400}
  $D::Type('tesuto',80);Start-Sleep -Milliseconds 700
  $display=Display;$state=Get-ResidentState
  $field=[System.Windows.Automation.AutomationElement]::FocusedElement;$bounds=$field.Current.BoundingRectangle
  $font=@(20,16,18)[$index]
  Check "field $index composition has a real caret" ($display['anchor'] -eq 'True') ($state['caret'])
  Check "field $index font matches field" ([math]::Abs([double]$display['fontDip']-$font) -lt 0.6) ($display['fontDip'])
  Check "field $index composition is inside focused field" ([double]$display['x'] -ge $bounds.Left-2 -and [double]$display['x'] -lt $bounds.Right -and [double]$display['y'] -ge $bounds.Top-5 -and [double]$display['y'] -lt $bounds.Bottom) ($display|ConvertTo-Json -Compress)
  $null=Save-WindowShot $app.Hwnd ('display/'+$Label+'-'+$index+'-composing.png')
  $shown=$state['text'];$D::Key(13);$null=Wait-Resident {param($s) $s['composing'] -eq 'False'};Start-Sleep -Milliseconds 500
  Check "field $index commit matches composition" ((Read-TestApp $app) -ceq $shown) (Read-TestApp $app)
  $null=Save-WindowShot $app.Hwnd ('display/'+$Label+'-'+$index+'-committed.png')
 }
} finally {Close-TestApp $app;$stopped=Stop-Resident $resident}
[ordered]@{Label=$Label;Checks=$checks;Resident=$stopped;At=(Get-Date).ToString('s')}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results ('display-'+$Label+'.json'))
if(@($checks|Where-Object {!$_.Ok}).Count){exit 1}
