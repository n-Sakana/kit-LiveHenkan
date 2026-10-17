param([string]$App='', [string]$Label='long', [int]$Interval=15, [int]$Blocks=8, [switch]$ParseOnly, [switch]$ChecksOnly, [switch]$ParserParity)
. (Join-Path $PSScriptRoot 'TestLib.ps1')
if(!$App){$App=Join-Path $script:Repo 'app'}
$temp=Join-Path $script:Results 'temp';[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp
$null=Build-Henkan -App $App -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'LongCompositionChecks.cs'))
$table=Join-Path $App 'romaji.tsv'
if($ParserParity){
 $answers=[Henkan.Tests.LongCompositionChecks]::ParserCases($table,(Join-Path $PSScriptRoot 'mixed.tsv'),(Join-Path $script:Results ($Label+'-parser-user')))
 $answers|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $script:Results ($Label+'-parser.json'))
 "Parser cases: $($answers.Count)";exit 0
}
if($ChecksOnly){
 $checks=[Henkan.Tests.LongCompositionChecks]::Checks($table)
 $checks|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results ($Label+'-checks.json'))
 $failed=@($checks|Where-Object {!$_.Ok});$failed|Format-Table Name,Detail
 "Long composition: $($checks.Count-$failed.Count)/$($checks.Count)"
 if($failed.Count){exit 1};exit 0
}
$parse=[Henkan.Tests.LongCompositionChecks]::Parse($table)
$stream=if(!$ParseOnly){[Henkan.Tests.LongCompositionChecks]::Stream((Join-Path $script:Repo 'dist/numeric-20261017/Henkan/xsmall.jinen'),$table,(Join-Path $script:Results ($Label+'-user')),$Interval,$Blocks)}else{@()}
[ordered]@{Parse=$parse;Stream=$stream;Interval=$Interval;At=(Get-Date).ToString('o')}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $script:Results ($Label+'.json'))
$parse|Format-Table Keys,KeyP95,KeyMax
$stream|Format-Table Keys,OpenChars,PendingChars,CommittedChars,KeyP95,KeyMax,ConversionP95,ConversionMax
if(!$ParseOnly){
 $final=$stream[-1];"Expected text: $($final.ExpectedText); commit preserves preview: $($final.CommitPreserved)"
 if(!$final.ExpectedText -or !$final.CommitPreserved){exit 1}
}
