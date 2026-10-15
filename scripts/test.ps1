[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1') | Out-Null}
$qa=Join-Path $root ('.local\qa\'+[Guid]::NewGuid().ToString('N'))
$fixture=Join-Path $qa 'codex';$state=Join-Path $qa 'data';$temporary=Join-Path $qa 'temp'
foreach($path in @($qa,$fixture,$state,$temporary)){$null=New-Item -ItemType Directory -Path $path -Force}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object {$_.FullName})
$sources+=@(Join-Path $root '.local\build\BuildInfo.g.cs')
$refs=@('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
function Invoke-Fixture {
    param([string]$Executable,[string[]]$Arguments)
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$Executable
    $start.Arguments=(@($Arguments | ForEach-Object {'"'+$_+'"'}) -join ' ')
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory=$root
    $start.EnvironmentVariables['CODEX_HOME']=$fixture;$start.EnvironmentVariables['CODEX_LITE_HUD_DATA_HOME']=$state
    $start.EnvironmentVariables['TMP']=$temporary;$start.EnvironmentVariables['TEMP']=$temporary
    $process=[Diagnostics.Process]::Start($start)
    if(-not $process.WaitForExit(30000)){$process.Kill();throw "Fixture timed out: $Executable"}
    if($process.ExitCode -ne 0){throw "Fixture failed with exit $($process.ExitCode): $Executable"}
}
$builder=Join-Path $qa 'FixtureBuilder.exe'
& $compiler /nologo /target:exe /platform:x64 "/out:$builder" /reference:System.Web.Extensions.dll (Join-Path $root 'tests\FixtureBuilder.cs')
if($LASTEXITCODE -ne 0){throw 'Fixture builder compilation failed'}
Invoke-Fixture $builder @($fixture)
$reader=Join-Path $qa 'ReaderChecks.exe'
& $compiler /nologo /define:READER_CHECKS /target:exe /platform:x64 "/out:$reader" /reference:System.Web.Extensions.dll (Join-Path $root 'src\SessionReader.cs') (Join-Path $root 'src\SessionCounters.cs') (Join-Path $root 'src\SessionTiming.cs') (Join-Path $root 'tests\ReaderChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Reader checks compilation failed'}
Invoke-Fixture $reader @()
$version=[IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
foreach($name in @('AppPathsChecks','ResponsivenessChecks','AppearanceChecks','QuotaChecks','QuotaFreshnessChecks','QuotaIoChecks','InteractionChecks','MenuResourceChecks','SessionCounterChecks','SessionTimingChecks','NameLayoutChecks','DetectionChecks','LayoutChecks','MovementChecks','VisibilityChecks')){
    $exe=Join-Path $qa ($name+'.exe');$report=Join-Path $qa ($name+'.txt')
    & $compiler /nologo /target:winexe /platform:x64 "/main:$name" "/out:$exe" $refs $sources (Join-Path $root "tests\$name.cs")
    if($LASTEXITCODE -ne 0){throw "Check compilation failed: $name"}
    $arguments=@($report)
    if($name -eq 'LayoutChecks'){$arguments=@('00000000-0000-7000-8000-000000000001',$report)}
    if($name -eq 'AppPathsChecks'){$arguments=@($report,$version)}
    if($name -eq 'MovementChecks'){$arguments=@($report,'100')}
    try{Invoke-Fixture $exe $arguments}finally{if(Test-Path -LiteralPath $report){Get-Content -LiteralPath $report}}
    if($name -eq 'LayoutChecks'){
        $smallReport=Join-Path $qa 'LayoutSmallDesktopChecks.txt'
        try{Invoke-Fixture $exe @('00000000-0000-7000-8000-000000000001',$smallReport,'700')}finally{if(Test-Path -LiteralPath $smallReport){Get-Content -LiteralPath $smallReport}}
    }
}
$app=Join-Path $root '.local\build\CodexLiteHud.exe';$info=Join-Path $qa 'info.json';$usage=Join-Path $qa 'usage.json'
Invoke-Fixture $app @('--check-info',$info)
Invoke-Fixture $app @('--check-session','00000000-0000-7000-8000-000000000001',$usage)
$metadata=Get-Content -LiteralPath $info -Raw | ConvertFrom-Json
$readings=Get-Content -LiteralPath $usage -Raw | ConvertFrom-Json
if($metadata.name -ne 'Codex Lite HUD' -or $metadata.version -ne $version -or $metadata.codexHome -ne $fixture -or $metadata.dataFolder -ne $state){throw 'Clean binary path/product check failed'}
if($readings.input -ne 53000000 -or $readings.output -ne 200000 -or $readings.accountQuota.windows[0].remaining -ne 26 -or $readings.turns -ne 1 -or $readings.completedTurns -ne 1 -or $readings.compactions -ne 1 -or $readings.runningMs -ne 4000 -or $readings.spanMs -lt 4000){throw 'Clean binary fixture usage/counters/timing differ'}
Write-Output 'PASS clean EXE, isolated CODEX_HOME, custom data folder and fixture token/quota/turn/compaction/time readings.'
