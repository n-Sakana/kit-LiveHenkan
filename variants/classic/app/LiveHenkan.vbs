' Runs LiveHenkan.ps1 without showing a console. Called from the right-click menu.
' This file must stay pure ASCII: wscript.exe reads a .vbs in the system ANSI code page.
Option Explicit
Dim fs, sh, appDir, ps, native, cmd, arg

If WScript.Arguments.Count <> 1 Then WScript.Quit 2
arg = WScript.Arguments(0)
Select Case arg
    Case "on", "off", "toggle", "word", "settings"
    Case Else
        WScript.Quit 2
End Select

Set fs = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
appDir = fs.GetParentFolderName(WScript.ScriptFullName)
ps = sh.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
' A 32-bit script host is redirected away from System32; Sysnative reaches the 64-bit PowerShell.
native = sh.ExpandEnvironmentStrings("%SystemRoot%") & "\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
If fs.FileExists(native) Then ps = native

cmd = Chr(34) & ps & Chr(34) & " -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File " _
    & Chr(34) & appDir & "\LiveHenkan.ps1" & Chr(34) & " -Command " & arg
sh.CurrentDirectory = appDir
sh.Run cmd, 0, False
