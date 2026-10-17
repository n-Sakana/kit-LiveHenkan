. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'CoreChecks.cs'),(Join-Path $PSScriptRoot 'FormattingChecks.cs'))
$rows=@(foreach($enabled in @($false,$true,$true,$false)){
 $samples=[Henkan.Tests.FormattingChecks]::Measure((Join-Path $script:Repo 'app/romaji.tsv'),(Join-Path $script:Results ('format-bench-'+[guid]::NewGuid().ToString('N'))),$enabled,500)
 [pscustomobject]@{Enabled=$enabled;Keys=$samples.Count;P50=(Get-Percentile $samples 0.5);P95=(Get-Percentile $samples 0.95);Max=(Get-Percentile $samples 1)}
})
$rows|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $script:Results 'formatting-measure.json')
$rows|Format-Table
