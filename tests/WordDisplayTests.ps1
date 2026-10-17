param([string]$ModelDir,[string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$Label='current')
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Initialize-Tests
$D=[Henkan.Tests.Driver];$resident=$null;$app=$null;$rows=@()
function InkBox($path,$x,$y,$width,$height){
 $bitmap=[Drawing.Bitmap]::new($path);$top=[int]::MaxValue;$bottom=-1
 try{for($row=$y;$row -lt $y+$height;$row++){for($col=$x;$col -lt $x+$width;$col++){$c=$bitmap.GetPixel($col,$row);if($c.R -lt 90 -and $c.G -lt 90 -and $c.B -lt 90){$top=[math]::Min($top,$row);$bottom=[math]::Max($bottom,$row)}}}}finally{$bitmap.Dispose()}
 return @($top,$bottom)
}
try {
 $app=Open-TestApp 'word';$D::TargetWindow=$app.Hwnd
 $resident=Start-Resident -Root $Root -ModelDir $ModelDir -UserDir (Join-Path $script:Results ('word-display-user-'+$Label+'-'+(Get-Date -Format HHmmss)))
 Start-Sleep -Milliseconds 1500
 foreach($setting in @(@(10,100),@(24,100),@(10,150))){
  $null=Send-Resident 'reset';Reset-TestApp $app
  $app.State.Word.Selection.Font.Name='Yu Gothic UI';$app.State.Word.Selection.Font.Size=$setting[0];$app.State.Word.ActiveWindow.View.Zoom.Percentage=$setting[1]
  Start-Sleep -Milliseconds 400
  $D::Type('tesuto',80);Start-Sleep -Milliseconds 650
  $state=Get-ResidentState;$display=Send-Resident 'display';$style=$D::Style()
  $shotName='display/word-'+$Label+'-'+$setting[0]+'-'+$setting[1]
  $before=Save-WindowShot $app.Hwnd ($shotName+'-composing.png')
  $D::Key(13);$null=Wait-Resident {param($s) $s['composing'] -eq 'False'};Start-Sleep -Milliseconds 500
  $actual=Read-TestApp $app
  $after=Save-WindowShot $app.Hwnd ($shotName+'-committed.png')
  $rect=$D::Rect($app.Hwnd);$c=$state['caret'].Substring($state['caret'].IndexOf(':')+1).Split(',')
  $x=[int]$c[0]-$rect[0]+2;$y=[int]$c[1]-$rect[1]-8
  $a=InkBox $before $x $y 12 ([int]$c[2]+16);$b=InkBox $after $x $y 12 ([int]$c[2]+16)
  # WPF and Word rasterize small Mincho glyphs differently. Check the actual
  # font size independently, and allow up to five pixels of small-glyph ink variance.
  $fields=@{};foreach($part in $display.Split("`t")){$p=$part.IndexOf('=');if($p -gt 0){$fields[$part.Substring(0,$p)]=$part.Substring($p+1)}}
  $expected=$setting[0]*$setting[1]/100*96/72
  $fontMatch=[math]::Abs([double]$fields['fontDip']-$expected) -lt 0.01
  $tolerance=if($expected -lt 16){5}else{2}
  $aligned=$a[1] -ge 0 -and $b[1] -ge 0 -and [math]::Abs($a[0]-$b[0]) -le 3 -and [math]::Abs($a[1]-$b[1]) -le $tolerance
  $rows += [pscustomobject]@{Points=$setting[0];Zoom=$setting[1];Caret=$state['caret'];Style=$style;Display=$display;InkBefore=$a;InkAfter=$b;FontMatch=$fontMatch;Aligned=$aligned;CommitMatch=($actual -ceq $state['text'])}
  $rows[-1]|ConvertTo-Json -Compress
 }
} finally {Close-TestApp $app;$stopped=Stop-Resident $resident}
$rows|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 (Join-Path $script:Results ('word-display-'+$Label+'.json'))
if(@($rows|Where-Object {!$_.CommitMatch -or !$_.Aligned -or !$_.FontMatch}).Count){exit 1}
