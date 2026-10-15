[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$version=[IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
$name="codex-lite-hud-$version-win-x64";$zip=Join-Path $root "dist\$name.zip"
$expected=@('CodexLiteHud.exe','README.md','README-EN.md','first-run.md','CHANGELOG.md','LICENSE','VERSION','install-shortcuts.ps1','portable.flag','docs/DATA.md','docs/DEVELOPMENT.md','docs/PERFORMANCE.md')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
    $files=@($archive.Entries | Where-Object {-not $_.FullName.EndsWith('/')})
    $actual=@($files | ForEach-Object {$_.FullName.Replace('\','/')})
    $allowed=@($expected | ForEach-Object {$name+'/'+$_})
    if(@(Compare-Object $allowed $actual).Count -ne 0){throw 'Release ZIP file set differs from the public allowlist'}
}finally{$archive.Dispose()}
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$line=[IO.File]::ReadAllText($zip+'.sha256').Trim()
if($line -ne ($hash+'  '+[IO.Path]::GetFileName($zip))){throw 'SHA-256 file does not match release ZIP'}
$check=Join-Path $root ('.local\zip-check-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $zip -DestinationPath $check
$exe=Join-Path $check "$name\CodexLiteHud.exe"
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if($info.ProductName -ne 'Codex Lite HUD' -or $info.ProductVersion -ne $version){throw 'EXE product metadata differs from VERSION'}
if([IO.File]::ReadAllText((Join-Path $check "$name\VERSION")).Trim() -ne $version){throw 'ZIP VERSION differs'}
$probe=Join-Path $check 'info.json'
$start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$exe;$start.Arguments='--check-info "'+$probe+'"'
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$start.EnvironmentVariables.Remove('CODEX_LITE_HUD_DATA_HOME');$start.EnvironmentVariables['CODEX_HOME']=Join-Path $check 'empty-codex'
$process=[Diagnostics.Process]::Start($start)
if(-not $process.WaitForExit(10000)){$process.Kill();throw 'Fresh ZIP EXE timed out'}
if($process.ExitCode -ne 0){throw 'Fresh ZIP EXE failed'}
$metadata=Get-Content -LiteralPath $probe -Raw | ConvertFrom-Json
if($metadata.dataFolder -ne (Join-Path $check "$name\data") -or $metadata.version -ne $version){throw 'Fresh ZIP portable directory/version differs'}
Write-Output "PASS clean release: $($expected.Count) allowlisted files, product metadata, SHA-256 and fresh portable EXE verified."
