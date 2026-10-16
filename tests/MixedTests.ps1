param([string]$ModelDir='',[int]$Threads=4)
# Headless: no windows, registration, hooks, clipboard access or resident instance.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-LiveHenkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'CoreChecks.cs'),(Join-Path $PSScriptRoot 'MixedChecks.cs'))
$model=Resolve-Model -Root $script:Repo -ModelDir (Get-TestModelDir $ModelDir)
$scratch=Join-Path $script:Results 'mixed-user'
if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}
$watch=[Diagnostics.Stopwatch]::StartNew()
$checks=[LiveHenkan.Tests.MixedChecks]::Run($model,(Join-Path $script:Repo 'app\romaji.tsv'),$scratch,(Join-Path $PSScriptRoot 'mixed.tsv'),$Threads)
$failed=@($checks|Where-Object{!$_.Ok})
foreach($c in $checks){if(!$c.Ok){"FAIL {0}: {1}" -f $c.Name,$c.Detail}}
[ordered]@{Checks=$checks;Failed=$failed.Count;Total=$checks.Count;Seconds=[math]::Round($watch.Elapsed.TotalSeconds,1);At=(Get-Date).ToString('s')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 (Join-Path $script:Results 'mixed-tests.json')
"mixed tests: $($checks.Count-$failed.Count)/$($checks.Count) in $([math]::Round($watch.Elapsed.TotalSeconds,1)) s"
if($failed.Count){exit 1}
