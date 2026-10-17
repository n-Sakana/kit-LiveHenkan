param([string]$ModelDir,[string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$Label='current',[string]$Reference='')
# Requires an approved desktop time slot. Uses only its own new Edge/profile/resident.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
Add-Type -Path (Join-Path $PSScriptRoot 'FontPixels.cs') -ReferencedAssemblies System.Drawing
$D=[Henkan.Tests.Driver];$resident=$null;$app=$null;$rows=@()
$referenceRows=if($Reference){Get-Content -LiteralPath $Reference -Raw -Encoding UTF8|ConvertFrom-Json}else{@()}
try {
 $resident=Start-Resident -Root $Root -ModelDir $ModelDir -UserDir (Join-Path $script:Results ('font-user-'+$Label+'-'+(Get-Date -Format HHmmss)))
 $app=Open-TestApp 'edge' 'web-font.html';$D::TargetWindow=$app.Hwnd
 Start-Sleep -Milliseconds 1200
 foreach($index in 0,1,2,3,4,5,6){
  if($index -gt 0){$D::Key(9);Start-Sleep -Milliseconds 400}
  $prefix=Read-TestApp $app
  $D::Type('tsukaigatte',100);Start-Sleep -Milliseconds 650
  $state=Get-ResidentState;$display=Send-Resident 'display';$fields=@{}
  foreach($part in $display.Split("`t")){$p=$part.IndexOf('=');if($p -gt 0){$fields[$part.Substring(0,$p)]=$part.Substring($p+1)}}
  $style=$D::Style();$bounds=[Windows.Automation.AutomationElement]::FocusedElement.Current.BoundingRectangle
  $name='display/font-'+$Label+'-'+$index
  $before=Save-WindowShot $app.Hwnd ($name+'-composing.png')
  $D::Key(13);$null=Wait-Resident {param($s) $s['composing'] -eq 'False'};Start-Sleep -Milliseconds 450
  $actual=Read-TestApp $app;$after=Save-WindowShot $app.Hwnd ($name+'-committed.png')
  $expectedSize=@(16,16,16,16,20,18,16)[$index];$expectedWeight=@(400,400,500,600,400,400,400)[$index]
  $fontMatch=[math]::Abs([double]$fields['fontDip']-$expectedSize) -lt 0.1
  $weightMatch=$fields['weight'] -eq [string]$expectedWeight
  $inside=$fields['anchor'] -eq 'True' -and [double]$fields['x'] -ge $bounds.Left -and [double]$fields['x'] -lt $bounds.Right -and [double]$fields['y'] -ge $bounds.Top-2 -and [double]$fields['y'] -lt $bounds.Bottom
  $rect=$D::Rect($app.Hwnd);$scale=[double]$fields['scale'];$x=[int]$fields['x']-$rect[0];$y=[int]$fields['y']-$rect[1]-4
  $width=[int][math]::Floor($expectedSize*$scale*$state['text'].Length)-1;$height=[int]$fields['height']+4
  $inkBefore=[Henkan.Tests.FontPixels]::Read($before,$x,$y,$width,$height,($index -in @(0,3)))
  $inkAfter=[Henkan.Tests.FontPixels]::Read($after,$x,$y,$width,$height,($index -in @(0,3)))
  $coverageRatio=if($inkAfter.Coverage -gt 0){$inkBefore.Coverage/$inkAfter.Coverage}else{0}
  $centerDelta=$inkBefore.CenterY-$inkAfter.CenterY
  $inkMatch=$coverageRatio -ge .9 -and $coverageRatio -le 1.1 -and [math]::Abs($centerDelta) -le 2
  # Explicit Japanese fonts are intentionally unchanged. Compare their rendering
  # with the original build instead of concealing existing WPF/browser leading differences.
  if($Reference -and $index -in @(4,5)){
   $original=@($referenceRows|Where-Object {$_.Index -eq $index})[0]
   $inkMatch=[math]::Abs($coverageRatio-$original.CoverageRatio) -lt .02 -and [math]::Abs($centerDelta-$original.CenterDelta) -lt 1
  }
  $rows += [pscustomobject]@{Index=$index;Style=$style;Display=$display;Prefix=$prefix;Text=$state['text'];Actual=$actual;FontMatch=$fontMatch;WeightMatch=$weightMatch;Inside=$inside;CommitMatch=($actual -ceq ($prefix+$state['text']));WindowRect=$rect;Before=$before;After=$after;InkBefore=$inkBefore;InkAfter=$inkAfter;CoverageRatio=$coverageRatio;CenterDelta=$centerDelta;InkMatch=$inkMatch}
  $rows[-1]|ConvertTo-Json -Compress
 }
}finally{Close-TestApp $app;$stopped=Stop-Resident $resident}
$rows|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results ('web-font-'+$Label+'.json'))
if(@($rows|Where-Object {!$_.CommitMatch -or !$_.Inside -or !$_.FontMatch -or !$_.WeightMatch -or !$_.InkMatch}).Count){exit 1}
