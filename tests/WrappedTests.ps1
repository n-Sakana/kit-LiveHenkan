# No windows, hooks or foreground input; render only to an in-memory bitmap.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'CoreChecks.cs'),(Join-Path $PSScriptRoot 'WrappedChecks.cs'))
$checks=[Henkan.Tests.WrappedChecks]::Run()
$failed=@($checks|Where-Object {!$_.Ok})
foreach($c in $failed){"FAIL $($c.Name): $($c.Detail)"}
$checks|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results 'wrapped.json')
"Wrapped: $($checks.Count-$failed.Count)/$($checks.Count)"
if($failed.Count){exit 1}
