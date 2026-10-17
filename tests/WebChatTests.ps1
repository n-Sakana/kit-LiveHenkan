param([Parameter(Mandatory=$true)][datetime]$Deadline,[string]$ModelDir,[string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$Label='current')
# Requires an approved foreground slot. Real probe/rendering; no resident or hooks.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $Root 'app') -Extra @((Join-Path $PSScriptRoot 'Driver.cs'),(Join-Path $PSScriptRoot 'WebVisual.cs'))
[Henkan.Native]::PrepareProcess()
Add-Type -Path (Join-Path $PSScriptRoot 'FontPixels.cs') -ReferencedAssemblies System.Drawing
$D=[Henkan.Tests.Driver];$visual=$null;$app=$null;$rows=@()
function Guard {if((Get-Date) -ge $Deadline){throw 'Approved slot expired'};if($app -and $D::Foreground() -ne $app.Hwnd){throw 'Our test window lost focus; stopped input'}}
function Display { $map=@{};foreach($p in ($visual.Inspect()).Split("`t")){$at=$p.IndexOf('=');if($at -gt 0){$map[$p.Substring(0,$at)]=$p.Substring($at+1)}};return $map }
try {
 Guard
 $visual=New-Object Henkan.Tests.WebVisual
 $app=Open-TestApp 'edge' 'web-chat.html';$D::TargetWindow=$app.Hwnd
 Start-Sleep -Milliseconds 1000
 foreach($index in 0..12){
  Guard
  if($index -gt 0){$D::OwnKey(9,$false,$false);Start-Sleep -Milliseconds 500}
  $initial=Read-TestApp $app
  $shown=if($index -in @(3,8)){'こんな感じで'+('あ'*$(if($index -eq 8){150}else{35}))}elseif($index -in @(0,12)){'これはチームスでのテストね'}elseif($index -eq 9){'ちょっとずれるよね'}else{'使い勝手'}
  $null=$visual.Show($shown,$app.Hwnd)
  Start-Sleep -Milliseconds 350;Guard
  $state=@{text=$shown};$display=Display;$prefix=Read-TestApp $app
  $element=[Windows.Automation.AutomationElement]::FocusedElement;$bounds=$element.Current.BoundingRectangle
  $pattern=$null;$uiaRects=@();if($element.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern,[ref]$pattern)){$uiaRects=$pattern.DocumentRange.GetBoundingRectangles()}
  $style=$visual.Style;$rect=$D::Rect($app.Hwnd)
  $before=Save-WindowShot $app.Hwnd ('display/chat-'+$Label+'-'+$index+'-composing.png')
  Guard;$visual.Hide();[Henkan.Native]::SendText($shown,[Henkan.Native]::OwnMark)
  $wait=[Diagnostics.Stopwatch]::StartNew()
  do{Start-Sleep -Milliseconds 100;Guard;$actual=Read-TestApp $app}while($actual -cne ($prefix+$shown) -and $wait.ElapsedMilliseconds -lt 5000)
  Start-Sleep -Milliseconds 200
  $actual=Read-TestApp $app;$after=Save-WindowShot $app.Hwnd ('display/chat-'+$Label+'-'+$index+'-committed.png')
  $expectedSize=@(14,16,16,16,17.5,13.5,14,14,16,16,20,18,17.5)[$index]
  $expectedWeight=if($index -in @(1,2,3,8,9)){500}else{400}
  $fontMatch=[math]::Abs([double]$display['fontDip']-$expectedSize) -lt .35
  $weightMatch=$display['weight'] -eq [string]$expectedWeight
  $inside=[double]$display['x'] -ge $bounds.Left-1 -and [double]$display['y'] -ge $bounds.Top-1
  if($display.ContainsKey('width')){$inside=$inside -and ([double]$display['x']+[double]$display['width']) -le $bounds.Right+1 -and ([double]$display['y']+[double]$display['height']) -le $bounds.Bottom+1}
  $inkBefore=$null;$inkAfter=$null;$ratio=$null;$center=$null
  if($index -ne 8){
   $scale=[double]$display['scale'];$x=[int]$display['x']-$rect[0];$y=[int]$display['y']-$rect[1]-4
   $width=[int][math]::Floor($expectedSize*$scale*$state['text'].Length)-1;$height=[int]$display['height']+4
   if($index -eq 3){$width=[int][math]::Floor($bounds.Width-32);$height=[int]($bounds.Bottom-[double]$display['y'])+4}
   $inkBefore=[Henkan.Tests.FontPixels]::Read($before,$x,$y,$width,$height,($index -eq 2))
   $inkAfter=[Henkan.Tests.FontPixels]::Read($after,$x,$y,$width,$height,($index -eq 2))
   $ratio=if($inkAfter.Coverage -gt 0){$inkBefore.Coverage/$inkAfter.Coverage}else{0};$center=$inkBefore.CenterY-$inkAfter.CenterY
  }
  $inkMatch=$index -eq 8 -or ($ratio -ge .9 -and $ratio -le 1.1 -and [math]::Abs($center) -le 1.5)
  $wrapMatch=$index -notin @(3,8) -or ($display['wrapped'] -eq 'True' -and [int]$display['lines'] -ge 3 -and $(if($index -eq 3){[double]$display['scroll'] -eq 0}else{[double]$display['scroll'] -gt 0}))
  $row=[pscustomobject]@{Index=$index;Initial=$initial;Prefix=$prefix;Text=$state['text'];Actual=$actual;PrefixIntact=($prefix -ceq $initial);CommitMatch=($actual -ceq ($prefix+$state['text']));Style=$style;Display=$display;Bounds=$bounds;UiaRects=$uiaRects;FontMatch=$fontMatch;WeightMatch=$weightMatch;Inside=$inside;InkMatch=$inkMatch;WrapMatch=$wrapMatch;Before=$before;After=$after;InkBefore=$inkBefore;InkAfter=$inkAfter;CoverageRatio=$ratio;CenterDelta=$center}
  $rows+=$row
  "Chat $Label $index font=$fontMatch weight=$weightMatch inside=$inside commit=$($row.CommitMatch) coverage=$ratio center=$center wrapped=$($display['wrapped'])"
 }
}finally{if($visual){$visual.Dispose()};Close-TestApp $app;$rows|ConvertTo-Json -Depth 6|Set-Content -Encoding UTF8 (Join-Path $script:Results ('web-chat-'+$Label+'.json'))}
if(@($rows|Where-Object {!$_.CommitMatch -or !$_.PrefixIntact -or !$_.Inside -or !$_.FontMatch -or !$_.WeightMatch -or !$_.InkMatch -or !$_.WrapMatch}).Count){exit 1}
