param([string]$ModelDir='',[string]$Destination='',[string]$ClassicSource='')
# Creates two separate carried folders and zip files. Does not register, run or publish either application.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'app\Common.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$model=Resolve-Model -Root $PSScriptRoot -ModelDir $ModelDir
if(!$Destination){$Destination=Join-Path $PSScriptRoot ('dist\variants-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$destinationPath=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $destinationPath){throw "Destination exists; choose a new folder: $destinationPath"}
if(!$ClassicSource){$ClassicSource=Join-Path $destinationPath 'source\LiveHenkan-classic'}
$classicPath=[IO.Path]::GetFullPath($ClassicSource)
if(Test-Path -LiteralPath $classicPath){throw "Classic source exists; choose a new path: $classicPath"}
$bundledSource=Join-Path $PSScriptRoot 'variants\classic'
if(!(Test-Path -LiteralPath (Join-Path $bundledSource 'MakePortable.ps1') -PathType Leaf)){throw "Classic source is missing: $bundledSource"}
[IO.Directory]::CreateDirectory($classicPath)|Out-Null
Get-ChildItem -LiteralPath $bundledSource | Copy-Item -Destination $classicPath -Recurse
& (Join-Path $classicPath 'MakePortable.ps1') -ModelDir (Split-Path -Parent $model) -Destination (Join-Path $destinationPath 'LiveHenkan-classic')
& (Join-Path $PSScriptRoot 'MakePortable.ps1') -ModelDir (Split-Path -Parent $model) -Destination (Join-Path $destinationPath 'LiveHenkan-mixed')
function Hash([string]$path){$sha=[Security.Cryptography.SHA256]::Create();$file=[IO.File]::OpenRead($path);try{return ([BitConverter]::ToString($sha.ComputeHash($file))).Replace('-','')}finally{$file.Dispose();$sha.Dispose()}}
$summaries=New-Object System.Collections.Generic.List[object]
foreach($name in 'LiveHenkan-classic','LiveHenkan-mixed'){
 $folder=Join-Path $destinationPath $name
 $source=if($name -eq 'LiveHenkan-classic'){$classicPath}else{$PSScriptRoot}
 # The distributed program matches its source, and the two copies use the same model.
 foreach($file in @(Get-ChildItem -LiteralPath (Join-Path $source 'app') -File -Recurse)){
  $relative=$file.FullName.Substring($source.Length+1)
  if((Hash $file.FullName) -ne (Hash (Join-Path $folder $relative))){throw "Program mismatch: $name $relative"}
 }
 if((Hash $model) -ne (Hash (Join-Path $folder 'xsmall.jinen'))){throw "Model mismatch: $name"}
 $guide=[IO.File]::ReadAllText((Join-Path $folder 'README.txt'),[Text.Encoding]::UTF8)
 $guide=$guide.Substring($guide.IndexOf("`n")+1).TrimStart()
 $title=if($name -eq 'LiveHenkan-classic'){'LiveHenkan — 手動切替版'}else{'LiveHenkan — 日英混在版'}
 $difference=if($name -eq 'LiveHenkan-classic'){'かなと英数を手動で切り替えて使います。日英の自動判定はありません。'}else{'かな入力のまま日本語と英語を続けて打てます。F10/F6 で選び直した語の言語を覚えます。'}
 $readme=$title+"`r`n`r`n"+$difference+"`r`n`r`nWindows 10 / 11（64-bit）。追加ソフト・管理者権限・ネット接続は不要です。`r`n1. このフォルダをまるごと置き、install.bat を実行します。`r`n2. デスクトップの右クリック →「LiveHenkan」→「オンにする」。`r`n3. やめるときは「オフにする」。メニューを外すときは uninstall.bat。`r`nWindows 11 では「その他のオプションを確認」の中です。`r`n`r`n設定・辞書・学習は、このフォルダの user に保存します。もう一方の版へは書き込みません。`r`n二つを同時に常駐させることはできません。別の版を使うときは、今の版をオフにして、使う版の install.bat を実行してください。`r`n右クリックメニューは最後に install.bat を実行した版を起動します。`r`n`r`n以下に、キー操作・設定・制限をまとめています。`r`n`r`n"+$guide
 [IO.File]::WriteAllText((Join-Path $folder 'README.txt'),$readme,(New-Object Text.UTF8Encoding($true)))
 $manifest=@(Get-ChildItem -LiteralPath $folder -File -Recurse|Where-Object{$_.Name -ne 'manifest.json'}|Sort-Object FullName|ForEach-Object{[pscustomobject]@{Name=$_.FullName.Substring($folder.Length+1);Bytes=$_.Length;SHA256=(Hash $_.FullName)}})
 $manifest|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $folder 'manifest.json')
 $zip=Join-Path $destinationPath ($name+'.zip')
 [IO.Compression.ZipFile]::CreateFromDirectory($folder,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
 if((Get-Item -LiteralPath $zip).Length -ge 100MB){throw "Zip exceeds 100 MB: $name"}
 $summaries.Add([pscustomobject]@{Name=$name;Folder=$folder;Zip=$zip;ZipBytes=(Get-Item -LiteralPath $zip).Length;ZipSHA256=(Hash $zip);ModelSHA256=(Hash (Join-Path $folder 'xsmall.jinen'));Files=$manifest.Count})
}
$short="# LiveHenkan`r`n`r`n- LiveHenkan-classic.zip：かなと英数を手動で切り替える手動切替版。`r`n- LiveHenkan-mixed.zip：日本語と英語を続けて打つ版。F10/F6 の言語選択を学習します。`r`n`r`n使う zip をすべて展開し、その中の install.bat を実行して、右クリック →「LiveHenkan」→「オンにする」で始めます。`r`n設定と辞書は各フォルダ内に保存されます。同時には常駐できません。別の版を使うときは先にオフにし、使う版の install.bat を実行してください。`r`nWindows 10/11（64-bit）で、追加ソフト・管理者権限・ネット接続は不要です。詳しい手順は各フォルダの README.txt にあります。`r`n"
[IO.File]::WriteAllText((Join-Path $destinationPath 'README.md'),$short,(New-Object Text.UTF8Encoding($false)))
$summaries.ToArray()|ConvertTo-Json -Depth 4
