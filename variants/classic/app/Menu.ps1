# Registration of the right-click menu, shared by install and uninstall. ASCII only.
$script:MenuKey='LiveHenkan'
$script:MenuContexts=@('Software\Classes\DesktopBackground\Shell','Software\Classes\Directory\Background\shell')
$script:MenuItems=@(
 @{Id='01on';Label='menu.on';Command='on'},
 @{Id='02off';Label='menu.off';Command='off'},
 @{Id='03word';Label='menu.word';Command='word'},
 @{Id='04settings';Label='menu.settings';Command='settings'}
)

function Get-LiveHenkanMenuKeys {
 $found=@()
 foreach($context in $script:MenuContexts){
  $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($context+'\'+$script:MenuKey)
  if($key){$key.Dispose();$found+=('HKEY_CURRENT_USER\'+$context+'\'+$script:MenuKey)}
 }
 return $found
}

function Unregister-LiveHenkanMenu {
 foreach($context in $script:MenuContexts){
  $parent=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($context,$true)
  if($parent){
   try{$parent.DeleteSubKeyTree($script:MenuKey,$false)}finally{$parent.Dispose()}
  }
 }
}

function Register-LiveHenkanMenu([string]$App,[hashtable]$Strings){
 # Every row in this submenu counts toward the Explorer limit.
 if($script:MenuItems.Count -gt 16){throw 'A submenu may contain at most 16 rows'}
 $launcher=Join-Path $App 'LiveHenkan.vbs'
 if(!(Test-Path -LiteralPath $launcher -PathType Leaf)){throw "LiveHenkan.vbs not found: $launcher"}
 $wscript=Join-Path $env:SystemRoot 'System32\wscript.exe'
 Unregister-LiveHenkanMenu
 foreach($context in $script:MenuContexts){
  $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($context+'\'+$script:MenuKey)
  try{
   $key.SetValue('MUIVerb',$Strings['menu.root'])
   $key.SetValue('SubCommands','')
   foreach($item in $script:MenuItems){
    $entry=$key.CreateSubKey('shell\'+$item.Id)
    try{
     $entry.SetValue('MUIVerb',$Strings[$item.Label])
     $command=$entry.CreateSubKey('command')
     try{$command.SetValue('',('"{0}" "{1}" {2}' -f $wscript,$launcher,$item.Command))}finally{$command.Dispose()}
    }finally{$entry.Dispose()}
   }
  }finally{$key.Dispose()}
 }
}
