param([string]$ModelDir='',[string]$Root='',[int]$IdleSeconds=20)
# Memory and processor use of the resident while nothing is typed, and how long it takes to start.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
if(!$Root){$Root=$script:Repo}
$modelDir=if(Test-Path -LiteralPath (Join-Path $Root 'xsmall.jinen')){''}else{Get-TestModelDir $ModelDir}
$resident=$null
try{
 $resident=Start-Resident -Root $Root -ModelDir $modelDir -Real
 Start-Sleep -Seconds 4
 $p=Get-Process -Id $resident.Process.Id
 $cpu0=$p.TotalProcessorTime.TotalMilliseconds
 Start-Sleep -Seconds $IdleSeconds
 $p.Refresh()
 $cpu1=$p.TotalProcessorTime.TotalMilliseconds
 $result=[ordered]@{
  StartMs=$resident.StartMs
  WorkingSetMB=[math]::Round($p.WorkingSet64/1MB,0);PrivateMB=[math]::Round($p.PrivateMemorySize64/1MB,0);Threads=$p.Threads.Count
  IdleSeconds=$IdleSeconds;IdleCpuMs=[math]::Round($cpu1-$cpu0,0);IdleCpuPercentOfOneCore=[math]::Round(($cpu1-$cpu0)/($IdleSeconds*10),2)
  At=(Get-Date).ToString('s')
 }
 $result|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $script:Results 'footprint.json')
 "start $($result.StartMs) ms; working set $($result.WorkingSetMB) MB; private $($result.PrivateMB) MB; idle CPU $($result.IdleCpuMs) ms in $IdleSeconds s ($($result.IdleCpuPercentOfOneCore)% of one core)"
}finally{"resident: "+(Stop-Resident $resident)}
