param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$ModelDir,[string]$Label='current')
# No windows or resident: compare identical paced input, including the pauses between words.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $Root 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'))
$model=Resolve-Model -Root $Root -ModelDir $ModelDir
$h=New-Object Henkan.Tests.Headless($model,(Join-Path $Root 'app/romaji.tsv'),3,(Join-Path $script:Results ('resource-user-'+$Label)))
$proc=[Diagnostics.Process]::GetCurrentProcess()
try {
 $h.Type('junbi',70);$h.Wait(1000);$null=$h.Key(13,$false);$h.Wait(1000)
 $samples=@()
 foreach($word in @('kaigi','shigoto','ashita','tenki','kikann','henkan')) {
  $start=$proc.TotalProcessorTime.TotalMilliseconds;$watch=[Diagnostics.Stopwatch]::StartNew()
  $h.Type($word,170);$h.Wait(900)
  $proc.Refresh();$cpu=$proc.TotalProcessorTime.TotalMilliseconds-$start
  $samples += [pscustomobject]@{Word=$word;CpuMs=[math]::Round($cpu);ElapsedMs=$watch.ElapsedMilliseconds;Text=$h.Text}
  $null=$h.Key(13,$false);$h.Wait(150)
 }
 $proc.Refresh();$start=$proc.TotalProcessorTime.TotalMilliseconds;$h.Wait(5000);$proc.Refresh()
 $result=[ordered]@{Label=$Label;Samples=$samples;CpuMs=($samples|Measure-Object CpuMs -Sum).Sum;Idle5sCpuMs=[math]::Round($proc.TotalProcessorTime.TotalMilliseconds-$start);Conversions=$h.Converter.Completed;Cancelled=$h.Converter.Cancelled;WorkingSetMB=[math]::Round($proc.WorkingSet64/1MB);At=(Get-Date).ToString('s')}
 $result | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $script:Results ('resources-'+$Label+'.json'))
 $result | ConvertTo-Json -Depth 5
} finally {$h.Dispose()}
