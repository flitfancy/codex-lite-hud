[CmdletBinding()]
param([switch]$StartWithWindows)
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'CodexLiteHud.exe'
if(-not (Test-Path -LiteralPath $exe)){throw 'Run this script from an extracted Codex Lite HUD release, beside CodexLiteHud.exe.'}
$shell=New-Object -ComObject WScript.Shell
$locations=@([Environment]::GetFolderPath('DesktopDirectory'))
if($StartWithWindows){$locations+=@([Environment]::GetFolderPath('Startup'))}
foreach($folder in $locations){
    $shortcut=$shell.CreateShortcut((Join-Path $folder 'Codex Lite HUD.lnk'))
    $shortcut.TargetPath=$exe;$shortcut.WorkingDirectory=$PSScriptRoot;$shortcut.Description='Codex Lite HUD';$shortcut.IconLocation=$exe+',0';$shortcut.Save()
}
Write-Output 'Shortcuts created. Keep the extracted folder in this location.'
