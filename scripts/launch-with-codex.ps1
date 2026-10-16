[CmdletBinding()]
param([switch]$Check)
$ErrorActionPreference='Stop'
try{
    $hud=Join-Path $PSScriptRoot 'CodexLiteHud.exe'
    if(-not (Test-Path -LiteralPath $hud)){throw 'CodexLiteHud.exe is missing. Keep this script beside the extracted HUD executable.'}
    $package=Get-AppxPackage -Name OpenAI.Codex | Select-Object -First 1
    if(-not $package){throw 'The Windows Codex app is not installed for this user.'}
    $manifest=Get-AppxPackageManifest -Package $package.PackageFullName
    $application=@($manifest.Package.Applications.Application | Where-Object {$_.Id -eq 'App' -or $_.Executable -match '(^|[\\/])ChatGPT\.exe$'}) | Select-Object -First 1
    if(-not $application){throw 'The Codex application entry could not be found.'}
    $appId=$package.PackageFamilyName+'!'+$application.Id
    $explorer=Join-Path $env:WINDIR 'explorer.exe'
    if($Check){[pscustomobject]@{Hud=$hud;CodexAppId=$appId;CodexTarget='shell:AppsFolder\'+$appId};return}
    Start-Process -FilePath $hud -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
    Start-Process -FilePath $explorer -ArgumentList ('shell:AppsFolder\'+$appId) -WindowStyle Normal
}
catch{
    if($Check){throw}
    Add-Type -AssemblyName System.Windows.Forms
    [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'Codex + HUD',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}
