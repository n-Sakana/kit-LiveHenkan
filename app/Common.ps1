# Shared by the launcher, the installer and the tests. ASCII only; Japanese text lives in strings.txt.
function Get-Strings {
 $map=@{}
 foreach($line in [IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'strings.txt'),[Text.Encoding]::UTF8)){
  if($line -match '^([A-Za-z][^=]*)=(.*)$'){$map[$matches[1].Trim()]=$matches[2]}
 }
 return $map
}

# One line to the resident and one line back. $null when no resident is listening.
function Send-Resident([string]$text,[int]$timeoutMs=400){
 $pipe=$null
 try{
  $pipe=New-Object System.IO.Pipes.NamedPipeClientStream('.',('Henkan.'+[Environment]::UserName),[System.IO.Pipes.PipeDirection]::InOut)
  $pipe.Connect($timeoutMs)
  $writer=New-Object System.IO.StreamWriter($pipe,(New-Object System.Text.UTF8Encoding($false)))
  $writer.WriteLine($text);$writer.Flush()
  $reader=New-Object System.IO.StreamReader($pipe,[System.Text.Encoding]::UTF8)
  $reply=$reader.ReadLine()
  if($null -eq $reply){$reply=''}
  return $reply
 }catch{return $null}
 finally{if($pipe){$pipe.Dispose()}}
}

# -ModelDir, then JINEN_MODEL_DIR, then the distributed folder itself.
function Resolve-Model([string]$Root,[string]$ModelDir='',[string]$Model='xsmall'){
 if(!$ModelDir -and $env:JINEN_MODEL_DIR){$ModelDir=$env:JINEN_MODEL_DIR}
 if(!$ModelDir){$ModelDir=$Root}
 $path=Join-Path $ModelDir ($Model+'.jinen')
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Model not found: $path"}
 return (Resolve-Path -LiteralPath $path).Path
}

# Compiles app\src\*.cs in memory with the compiler that ships with .NET Framework. Extra sources are for tests.
function Build-Henkan([string]$App,[string[]]$Extra=@()){
 $fw=[Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
 $wpf=Join-Path $fw 'WPF'
 $cp=New-Object System.CodeDom.Compiler.CompilerParameters
 $cp.GenerateInMemory=$true
 $cp.CompilerOptions='/unsafe /optimize'
 $cp.ReferencedAssemblies.AddRange(@('System.dll','System.Core.dll','System.Xml.dll','System.Numerics.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','Accessibility.dll',
  (Join-Path $wpf 'WindowsBase.dll'),(Join-Path $wpf 'PresentationCore.dll'),(Join-Path $wpf 'PresentationFramework.dll'),(Join-Path $wpf 'UIAutomationClient.dll'),(Join-Path $wpf 'UIAutomationTypes.dll')))
 $files=@(Get-ChildItem -LiteralPath (Join-Path $App 'src') -Filter '*.cs'|Sort-Object Name|ForEach-Object{$_.FullName})+$Extra
 $sources=@($files|ForEach-Object{[IO.File]::ReadAllText($_,[Text.Encoding]::UTF8)})
 $provider=New-Object Microsoft.CSharp.CSharpCodeProvider
 $result=$provider.CompileAssemblyFromSource($cp,[string[]]$sources)
 if($result.Errors.HasErrors){
  $messages=@($result.Errors|Where-Object{!$_.IsWarning}|ForEach-Object{$_.ToString()})
  throw ("Compile failed:`r`n"+($messages -join "`r`n"))
 }
 return $result.CompiledAssembly
}
