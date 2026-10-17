param([string]$ModelDir)
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'CoreChecks.cs'),(Join-Path $PSScriptRoot 'FormattingModelChecks.cs'))
$model=Resolve-Model -Root $script:Repo -ModelDir (Get-TestModelDir $ModelDir)
$scratch=Join-Path $script:Results ('formatting-model-user-'+[guid]::NewGuid().ToString('N'))
$checks=[Henkan.Tests.FormattingModelChecks]::Run($model,(Join-Path $script:Repo 'app/romaji.tsv'),$scratch)
$failed=@($checks|Where-Object {!$_.Ok})
foreach($c in $failed){"FAIL $($c.Name): $($c.Detail)"}
[ordered]@{Checks=$checks;Passed=($checks.Count-$failed.Count);Total=$checks.Count;At=(Get-Date).ToString('o')}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results 'formatting-model.json')
"Formatting model: $($checks.Count-$failed.Count)/$($checks.Count)"
if($failed.Count){exit 1}
