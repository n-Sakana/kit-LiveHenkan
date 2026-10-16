param([string]$ModelDir='',[string]$Out='',[int]$Threads=0,[switch]$SkipSmall)
# Measures engine speed by thread count, delay per keystroke, candidate delay, accuracy of both models.
# No windows are opened. Takes several minutes.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
if(!('LiveHenkan.Tests.Measure' -as [type])){
 $temp=Join-Path $script:Repo 'temp';[IO.Directory]::CreateDirectory($temp)|Out-Null;$env:TEMP=$temp;$env:TMP=$temp
 $null=Build-LiveHenkan -App (Join-Path $script:Repo 'app') -Extra @((Join-Path $PSScriptRoot 'Headless.cs'),(Join-Path $PSScriptRoot 'Driver.cs'),(Join-Path $PSScriptRoot 'Measure.cs'))
 [LiveHenkan.Native]::PrepareProcess()
}
$dir=Get-TestModelDir $ModelDir
if(!$Out){$Out=Join-Path $script:Results 'measure'}
[IO.Directory]::CreateDirectory($Out)|Out-Null
$table=Join-Path $script:Repo 'app\romaji.tsv'
$scratch=Join-Path $script:Results 'measure-user'
if($Threads -le 0){$n=[Environment]::ProcessorCount;$Threads=if($n -le 4){2}elseif($n -le 8){3}elseif($n -le 12){4}else{5}}
$M=[LiveHenkan.Tests.Measure]
$xsmall=Join-Path $dir 'xsmall.jinen';$small=Join-Path $dir 'small.jinen'
function Rows($sentences){@($sentences|ForEach-Object{[pscustomobject]@{Reading=$_.Reading;Accept=($_.Accept -join '|');Got=$_.Got;Ok=$_.Ok}})}
function Corpus36{
 $all=Get-Content -LiteralPath (Join-Path $script:Repo 'fixtures\corpus36.json') -Raw -Encoding UTF8|ConvertFrom-Json
 ,@($all|ForEach-Object{$s=New-Object LiveHenkan.Tests.Sentence;$s.Reading=$_.reading;$s.Accept=@($_.expected);$s})
}
$report=[ordered]@{At=(Get-Date).ToString('s');Machine=(Get-CimInstance Win32_Processor).Name;LogicalProcessors=[Environment]::ProcessorCount;Threads=$Threads}

"engine steps..."
$steps=@();foreach($t in 1,2,3,4,5,6){$steps+=$M::Steps($xsmall,$t)}
$report.Steps=$steps

"accuracy xsmall..."
$everyday=$M::Load((Join-Path $PSScriptRoot 'everyday.tsv'))
$keyMs=New-Object 'System.Collections.Generic.List[double]';$convMs=New-Object 'System.Collections.Generic.List[double]'
$a=$M::Accuracy($xsmall,$table,$scratch,$Threads,$everyday,70,$keyMs,$convMs)
$report.XsmallEveryday=[ordered]@{Ok=@($a|Where-Object{$_.Ok}).Count;Total=$a.Count;Rows=(Rows $a)}
$report.KeyToConverted70=$M::Summary('key to converted, 70 ms between keys',$keyMs)
$report.ConvertMs=$M::Summary('one conversion',$convMs)
$c36=Corpus36
$a=$M::Accuracy($xsmall,$table,$scratch,$Threads,$c36,0,$null,$null)
$report.XsmallCorpus36=[ordered]@{Ok=@($a|Where-Object{$_.Ok}).Count;Total=$a.Count;Rows=(Rows $a)}

"fast typing..."
$keyMs=New-Object 'System.Collections.Generic.List[double]'
$fast=$M::Load((Join-Path $PSScriptRoot 'everyday.tsv'))
$a=$M::Accuracy($xsmall,$table,$scratch,$Threads,$fast,30,$keyMs,$null)
$report.KeyToConverted30=$M::Summary('key to converted, 30 ms between keys',$keyMs)
$report.XsmallEverydayFast=[ordered]@{Ok=@($a|Where-Object{$_.Ok}).Count;Total=$a.Count}

"candidates..."
$readings=@([IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'candidates.txt'),[Text.Encoding]::UTF8)|Where-Object{$_ -and $_[0] -ne '#'})
$report.Candidates=$M::Candidates($xsmall,$table,$scratch,$Threads,[string[]]$readings)

if(!$SkipSmall -and (Test-Path -LiteralPath $small)){
 "accuracy small..."
 $convMs=New-Object 'System.Collections.Generic.List[double]'
 $e2=$M::Load((Join-Path $PSScriptRoot 'everyday.tsv'))
 $a=$M::Accuracy($small,$table,$scratch,$Threads,$e2,0,$null,$convMs)
 $report.SmallEveryday=[ordered]@{Ok=@($a|Where-Object{$_.Ok}).Count;Total=$a.Count;Rows=(Rows $a)}
 $report.SmallConvertMs=$M::Summary('one conversion, small model',$convMs)
 $c36=Corpus36
 $a=$M::Accuracy($small,$table,$scratch,$Threads,$c36,0,$null,$null)
 $report.SmallCorpus36=[ordered]@{Ok=@($a|Where-Object{$_.Ok}).Count;Total=$a.Count;Rows=(Rows $a)}
}
$report|ConvertTo-Json -Depth 6|Set-Content -Encoding UTF8 (Join-Path $Out 'measure.json')
"steps: "+(($steps|ForEach-Object{"$($_.Name) $($_.Median)"}) -join '; ')
"xsmall everyday $($report.XsmallEveryday.Ok)/$($report.XsmallEveryday.Total), corpus36 $($report.XsmallCorpus36.Ok)/$($report.XsmallCorpus36.Total)"
"key->converted @70ms: median $($report.KeyToConverted70.Median) p95 $($report.KeyToConverted70.P95) max $($report.KeyToConverted70.Max) (n=$($report.KeyToConverted70.N)); @30ms: median $($report.KeyToConverted30.Median) p95 $($report.KeyToConverted30.P95)"
"one conversion: median $($report.ConvertMs.Median) p95 $($report.ConvertMs.P95); candidates: median $($report.Candidates.Median) p95 $($report.Candidates.P95) max $($report.Candidates.Max)"
if($report.SmallEveryday){"small everyday $($report.SmallEveryday.Ok)/$($report.SmallEveryday.Total), corpus36 $($report.SmallCorpus36.Ok)/$($report.SmallCorpus36.Total); one conversion median $($report.SmallConvertMs.Median) p95 $($report.SmallConvertMs.P95)"}
