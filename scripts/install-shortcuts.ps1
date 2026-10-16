[CmdletBinding()]
param([switch]$StartWithWindows,[switch]$WithCodex)
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'CodexLiteHud.exe'
if(-not (Test-Path -LiteralPath $exe)){throw 'Run this script from an extracted Codex Lite HUD release, beside CodexLiteHud.exe.'}
$launcher=Join-Path $PSScriptRoot 'launch-with-codex.ps1'
if($WithCodex -and -not (Test-Path -LiteralPath $launcher)){throw 'launch-with-codex.ps1 is missing from the extracted release.'}
$shell=New-Object -ComObject WScript.Shell
$locations=@([Environment]::GetFolderPath('DesktopDirectory'))
if($StartWithWindows){$locations+=@([Environment]::GetFolderPath('Startup'))}
foreach($folder in $locations){
    $shortcut=$shell.CreateShortcut((Join-Path $folder 'Codex Lite HUD.lnk'))
    $shortcut.TargetPath=$exe;$shortcut.WorkingDirectory=$PSScriptRoot;$shortcut.Description='Codex Lite HUD';$shortcut.IconLocation=$exe+',0';$shortcut.Save()
}
if($WithCodex){
    $shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Codex + HUD.lnk'))
    $shortcut.TargetPath=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $shortcut.Arguments='-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$launcher+'"'
    $shortcut.WorkingDirectory=$PSScriptRoot;$shortcut.Description='Start Codex and Codex Lite HUD';$shortcut.IconLocation=$exe+',0';$shortcut.WindowStyle=7;$shortcut.Save()
}
Write-Output 'Shortcuts created. Keep the extracted folder in this location.'
