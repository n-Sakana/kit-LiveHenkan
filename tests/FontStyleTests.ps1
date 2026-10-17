param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$Label='current')
# No windows, hooks, screen capture, resident or model: font resolution only.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
Add-Type -AssemblyName PresentationCore,PresentationFramework
$scratch=Join-Path $script:Repo 'temp';$env:TEMP=$scratch;$env:TMP=$scratch
$null=Build-Henkan -App (Join-Path $Root 'app')
$checks=New-Object System.Collections.Generic.List[object]
function Check($name,$ok,$detail=''){$checks.Add([pscustomobject]@{Name=$name;Ok=[bool]$ok;Detail=$detail});"$ok $name $detail"}
foreach($weight in @(100,350,400,500,600,700,900)){
 $style=[Henkan.StyleInfo]::new();$style.ReadWeight($weight)
 Check "published weight $weight survives" ($style.Weight -eq $weight -and [Windows.FontWeight]::FromOpenTypeWeight($style.Weight).ToOpenTypeWeight() -eq $weight)
}
foreach($invalid in @(-1,0,1000,'500',$null)){
 $style=[Henkan.StyleInfo]::new();$style.ReadWeight($invalid)
 Check "invalid or unavailable weight uses normal: $invalid" ($style.Weight -eq 400)
}
$fallback=[Henkan.DocumentFonts]::WebJapanese
Check 'browser fallback is an installed Japanese font' ([Henkan.DocumentFonts]::HasJapanese($fallback)) $fallback
foreach($name in @('Segoe UI','Arial')){
 $style=[Henkan.StyleInfo]::new();$style.FontName=$name;$style.Web=$true
 Check "$name web Japanese follows browser fallback" ([Henkan.DocumentFonts]::Fallback($style) -eq $fallback) ([Henkan.DocumentFonts]::Family($style).Source)
 $style.Web=$false
 Check "$name native fallback stays unchanged" ([Henkan.DocumentFonts]::Fallback($style) -eq 'Yu Gothic UI')
}
foreach($name in @('Yu Gothic UI','Meiryo','Yu Mincho','MS Gothic')){
 $style=[Henkan.StyleInfo]::new();$style.FontName=$name;$style.Web=$true
 $old=if($name -eq 'Yu Mincho'){'Yu Mincho'}else{'Yu Gothic UI'}
 Check "$name explicit Japanese font stays unchanged" ([Henkan.DocumentFonts]::Family($style).Source -eq ($name+', '+$old+', Yu Gothic UI'))
}
Check 'missing font cannot masquerade as installed Japanese' (![Henkan.DocumentFonts]::HasJapanese('Henkan nonexistent font 918273'))
$alias=[Henkan.StyleInfo]::new();$alias.FontName='Segoe UI Semibold';$alias.Web=$true;$alias.Weight=500
Check 'a primary face alias cannot force bold Japanese fallback' ([Henkan.DocumentFonts]::Family($alias).Source.StartsWith('Segoe UI, ') -and $alias.Weight -eq 500)
$checks | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $script:Results ('font-style-'+$Label+'.json'))
if(@($checks|Where-Object {!$_.Ok}).Count){exit 1}
