param([string]$Label='formatting')
# Headless. Pure formatting plus real Composer key/candidate/commit transitions.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'CoreChecks.cs'),(Join-Path $PSScriptRoot 'FormattingChecks.cs'))
$cases=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'formatting.json') -Raw -Encoding UTF8|ConvertFrom-Json
$rows=@(foreach($case in $cases){
 $result=[Henkan.NumericFormatting]::Apply($case.input,[string]$case.left,[datetime]'2026-10-17')
 $twice=[Henkan.NumericFormatting]::Apply($result.Text,[string]$case.left,[datetime]'2026-10-17')
 [pscustomobject]@{Name=$case.input;Ok=($result.Text -ceq $case.expected -and $twice.Text -ceq $result.Text);Detail=('expected ['+$case.expected+'] actual ['+$result.Text+']');Edits=$result.Edits.Count}
})
$scratch=Join-Path $script:Results ($Label+'-user-'+[guid]::NewGuid().ToString('N'))
$checks=[Henkan.Tests.FormattingChecks]::Run((Join-Path $script:Repo 'app/romaji.tsv'),$scratch)
$all=@($rows)+@($checks)
foreach($row in $all){if(!$row.Ok){'FAIL '+$row.Name+': '+$row.Detail}}
[ordered]@{Pure=$rows;Composer=$checks;Passed=@($all|Where-Object Ok).Count;Total=$all.Count;At=(Get-Date).ToString('o')}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results ($Label+'.json'))
"Formatting: $(@($all|Where-Object Ok).Count)/$($all.Count)"
if(@($all|Where-Object {!$_.Ok}).Count){exit 1}
