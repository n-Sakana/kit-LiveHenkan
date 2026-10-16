param([string]$ModelDir='',[int]$Threads=4)
# Input logic with the real model, without windows or hooks. Safe to run while other work is on screen.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
if(!('LiveHenkan.Tests.CoreChecks' -as [type])){
 $temp=Join-Path $script:Repo 'temp';[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp
 $null=Build-LiveHenkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'Driver.cs'),(Join-Path $PSScriptRoot 'CoreChecks.cs'))
 [LiveHenkan.Native]::PrepareProcess()
}
$model=Resolve-Model -Root $script:Repo -ModelDir (Get-TestModelDir $ModelDir)
$scratch=Join-Path $script:Results 'core-user'
$watch=[Diagnostics.Stopwatch]::StartNew()
$checks=[LiveHenkan.Tests.CoreChecks]::Run($model,(Join-Path $script:Repo 'app\romaji.tsv'),$scratch,$Threads)
$failed=@($checks|Where-Object{!$_.Ok})
foreach($c in $checks){if(!$c.Ok){"FAIL {0}: {1}" -f $c.Name,$c.Detail}}
[ordered]@{Checks=$checks;Failed=$failed.Count;Total=$checks.Count;Seconds=[math]::Round($watch.Elapsed.TotalSeconds,1);At=(Get-Date).ToString('s')}|ConvertTo-Json -Depth 4|Set-Content -Encoding UTF8 (Join-Path $script:Results 'core-tests.json')
"core tests: $($checks.Count-$failed.Count)/$($checks.Count) in $([math]::Round($watch.Elapsed.TotalSeconds,1)) s"
if($failed.Count){exit 1}
