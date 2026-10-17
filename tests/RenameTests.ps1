param([string]$Root=(Split-Path -Parent $PSScriptRoot))
# Headless: reads fixture files written by the previous release, no UI or hooks.
. (Join-Path $PSScriptRoot 'TestLib.ps1')
$null=Build-Henkan -App (Join-Path $Root 'app')
$dir=Join-Path $script:Repo 'fixtures/user-before-rename'
$hashes=@(Get-ChildItem -LiteralPath $dir -File|Get-FileHash -Algorithm SHA256)
$checks=New-Object System.Collections.Generic.List[object]
function Check($name,$ok){$checks.Add([pscustomobject]@{Name=$name;Ok=[bool]$ok});"$ok $name"}
$dict=[Henkan.UserDictionary]::new($dir)
Check 'previous dictionary loads' ($dict.Words().Count -eq 1 -and $dict.Words()[0].Surface -eq 'sample@example.invalid')
Check 'previous candidate learning loads' ($dict.LearnedCount -eq 1 -and $dict.Find('testreading')[0].Surface -eq 'chosen candidate')
$languages=$dict.Languages.Snapshot()
Check 'Japanese preference survives' ($languages.ContainsKey('same') -and $languages['same'] -eq $false)
Check 'English preference survives' ($languages.ContainsKey('api') -and $languages['api'] -eq $true)
$settings=[Henkan.Settings]::new();[Henkan.SettingsStore]::Load((Join-Path $dir 'settings.txt'),$settings)
Check 'settings survive' ($settings.Threads -eq 2 -and !$settings.Learn)
foreach($item in $hashes){Check ('reading leaves '+(Split-Path $item.Path -Leaf)+' unchanged') ((Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash -eq $item.Hash)}
Check 'new IPC name' ([Henkan.App]::PipeName -eq ('Henkan.'+[Environment]::UserName))
$field=[Henkan.App].GetField('PreviousMutexName',[Reflection.BindingFlags]'NonPublic,Static')
$created=$false;$mutex=[Threading.Mutex]::new($true,[string]$field.GetRawConstantValue(),[ref]$created)
try{
 if(!$created){throw 'A previous resident exists; refusing to test against it'}
 Check 'previous resident prevents new startup before model or UI' ([Henkan.App]::Run('unused','missing-model','on',$true,'unused') -eq 2)
}finally{if($created){$mutex.ReleaseMutex()};$mutex.Dispose()}
$checks|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $script:Results 'rename-tests.json')
if(@($checks|Where-Object {!$_.Ok}).Count){exit 1}
