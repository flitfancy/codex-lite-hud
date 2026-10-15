[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1') | Out-Null}
$version=[IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
$name="codex-lite-hud-$version-win-x64"
$staging=Join-Path $root ('.local\package-'+[Guid]::NewGuid().ToString('N'))
$folder=Join-Path $staging $name
$null=New-Item -ItemType Directory -Path $folder -Force
Copy-Item -LiteralPath (Join-Path $root '.local\build\CodexLiteHud.exe') -Destination $folder
foreach($file in @('README.md','README-EN.md','first-run.md','CHANGELOG.md','LICENSE','VERSION')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination $folder}
foreach($file in @('docs\DATA.md','docs\DEVELOPMENT.md','docs\PERFORMANCE.md')){
    $destination=Join-Path $folder $file
    $null=New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-shortcuts.ps1') -Destination $folder
[IO.File]::WriteAllText((Join-Path $folder 'portable.flag'),'Store user settings in the adjacent data folder.'+[Environment]::NewLine)
$dist=Join-Path $root 'dist';$null=New-Item -ItemType Directory -Path $dist -Force
$zip=Join-Path $dist ($name+'.zip')
Compress-Archive -LiteralPath $folder -DestinationPath $zip -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip+'.sha256',$hash+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine,[Text.Encoding]::ASCII)
Write-Output $zip
