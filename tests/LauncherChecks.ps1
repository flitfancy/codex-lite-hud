param([string]$Work,[string]$Report)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=Join-Path $Work 'launcher with spaces';$null=New-Item -ItemType Directory -Path $folder -Force
$launcher=Join-Path $folder 'launch-with-codex.ps1'
Copy-Item -LiteralPath (Join-Path $root 'scripts\launch-with-codex.ps1') -Destination $launcher
[IO.File]::WriteAllText((Join-Path $folder 'CodexLiteHud.exe'),'fixture - never executed')
$global:HudLauncherChecksCalls=[Collections.Generic.List[object]]::new()
$global:HudLauncherChecksInstalled=$true
function global:Get-AppxPackage {
    param([string]$Name)
    if($global:HudLauncherChecksInstalled){[pscustomobject]@{PackageFullName='fixture-version';PackageFamilyName='OpenAI.Codex_fixture'}}
}
function global:Get-AppxPackageManifest {
    param([string]$Package)
    [pscustomobject]@{Package=[pscustomobject]@{Applications=[pscustomobject]@{Application=@([pscustomobject]@{Id='App';Executable='app/ChatGPT.exe'},[pscustomobject]@{Id='CommandRunner';Executable='app/runner.exe'})}}}
}
function global:Start-Process {
    [CmdletBinding()]
    param([string]$FilePath,[string]$ArgumentList,[string]$WorkingDirectory,[string]$WindowStyle)
    $global:HudLauncherChecksCalls.Add([pscustomobject]@{file=$FilePath;arguments=$ArgumentList;folder=$WorkingDirectory;style=$WindowStyle})
}
try{
    $plan=& $launcher -Check
    if($plan.CodexAppId -ne 'OpenAI.Codex_fixture!App' -or $global:HudLauncherChecksCalls.Count -ne 0){throw 'Check mode activated an application or chose the wrong entry'}
    & $launcher
    $calls=$global:HudLauncherChecksCalls
    if($calls.Count -ne 2 -or $calls[0].file -ne (Join-Path $folder 'CodexLiteHud.exe') -or $calls[0].folder -ne $folder -or $calls[1].arguments -ne 'shell:AppsFolder\OpenAI.Codex_fixture!App' -or $calls[0].style -ne 'Hidden' -or $calls[1].style -ne 'Normal'){throw 'Joint launch plan differs'}
    $global:HudLauncherChecksInstalled=$false;$caught=$false
    try{& $launcher -Check}catch{$caught=$true}
    if(-not $caught -or $calls.Count -ne 2){throw 'Missing Codex must fail before launching either program'}
    [IO.File]::WriteAllText($Report,'PASS joint launch entry, paths with spaces, hidden HUD launcher, normal Codex activation, read-only check mode and missing Codex guard. No real programs activated.')
}catch{
    [IO.File]::WriteAllText($Report,$_.ToString());throw
}
