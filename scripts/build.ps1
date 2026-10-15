[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root '.local\build'}
$null=New-Item -ItemType Directory -Path $OutputDirectory -Force
$version=[IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
if($version -notmatch '^\d+\.\d+(\.\d+)?$'){throw 'VERSION must contain a numeric release such as 1.0'}
$parts=$version.Split('.');while($parts.Count -lt 4){$parts+=@('0')};$assemblyVersion=$parts -join '.'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.8 Windows compiler is required'}
$generated=Join-Path $OutputDirectory 'BuildInfo.g.cs'
[IO.File]::WriteAllText($generated,"static class BuildInfo { internal const string Version = `"$version`"; internal const string AssemblyVersion = `"$assemblyVersion`"; }",[Text.UTF8Encoding]::new($false))
$manifest=Join-Path $OutputDirectory 'app.manifest'
$xml=[IO.File]::ReadAllText((Join-Path $root 'assets\app.manifest')) -replace 'version="1\.0\.0\.0" name="CodexLiteHud"',("version=`"$assemblyVersion`" name=`"CodexLiteHud`"")
[IO.File]::WriteAllText($manifest,$xml,[Text.UTF8Encoding]::new($false))
$icon=Join-Path $root 'assets\icon.ico'
if(-not (Test-Path -LiteralPath $icon)){
    $maker=Join-Path $OutputDirectory 'MakeIcon.exe'
    & $compiler /nologo /target:exe /platform:x64 "/out:$maker" /reference:System.Drawing.dll (Join-Path $root 'scripts\MakeIcon.cs')
    if($LASTEXITCODE -ne 0){throw 'Icon tool build failed'}
    & $maker $icon
    if($LASTEXITCODE -ne 0){throw 'Icon creation failed'}
}
$output=Join-Path $OutputDirectory 'CodexLiteHud.exe'
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object {$_.FullName})
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" "/win32manifest:$manifest" "/win32icon:$icon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $sources $generated
if($LASTEXITCODE -ne 0){throw 'Codex Lite HUD compilation failed'}
Write-Output $output
