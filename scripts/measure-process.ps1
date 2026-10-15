[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ProcessId,[int]$Seconds=30,[string]$Report)
$ErrorActionPreference='Stop'
if(-not ('HudProcessIo' -as [type])){
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HudProcessIo {
    [StructLayout(LayoutKind.Sequential)] public struct Counts {public ulong reads,writes,other,readBytes,writeBytes,otherBytes;}
    [DllImport("kernel32.dll")] public static extern bool GetProcessIoCounters(IntPtr handle,out Counts counts);
}
'@
}
$target=Get-Process -Id $ProcessId
$startCpu=$target.TotalProcessorTime.TotalMilliseconds
$startIo=[HudProcessIo+Counts]::new();$null=[HudProcessIo]::GetProcessIoCounters($target.Handle,[ref]$startIo)
$samples=[Collections.Generic.List[object]]::new();$clock=[Diagnostics.Stopwatch]::StartNew();$lastMs=0.0;$lastCpu=$startCpu
while($clock.Elapsed.TotalSeconds -lt $Seconds){
    Start-Sleep -Milliseconds 1000
    $target.Refresh();if($target.HasExited){throw 'Measured HUD process exited'}
    $at=$clock.Elapsed.TotalMilliseconds;$cpu=$target.TotalProcessorTime.TotalMilliseconds
    $samples.Add([pscustomobject]@{seconds=[math]::Round($at/1000,3);singleCorePercent=100*($cpu-$lastCpu)/($at-$lastMs);workingMiB=$target.WorkingSet64/1MB;privateMiB=$target.PrivateMemorySize64/1MB;handles=$target.HandleCount;threads=$target.Threads.Count})
    $lastMs=$at;$lastCpu=$cpu
}
$endIo=[HudProcessIo+Counts]::new();$null=[HudProcessIo]::GetProcessIoCounters($target.Handle,[ref]$endIo)
$elapsed=$clock.Elapsed.TotalSeconds;$sorted=@($samples.singleCorePercent | Sort-Object)
$result=[ordered]@{
    processId=$ProcessId;executable=$target.Path;capturedAt=(Get-Date -Format o);logicalProcessors=[Environment]::ProcessorCount;seconds=[math]::Round($elapsed,3)
    cpuSingleCoreAverage=[math]::Round(($lastCpu-$startCpu)/($elapsed*10),3)
    cpuMachineAverage=[math]::Round(($lastCpu-$startCpu)/($elapsed*10*[Environment]::ProcessorCount),4)
    cpuSingleCoreP95=[math]::Round($sorted[[math]::Max(0,[math]::Ceiling($sorted.Count*.95)-1)],3)
    workingMiBStart=[math]::Round($samples[0].workingMiB,2);workingMiBEnd=[math]::Round($samples[-1].workingMiB,2);workingMiBPeak=[math]::Round(($samples.workingMiB | Measure-Object -Maximum).Maximum,2)
    privateMiBStart=[math]::Round($samples[0].privateMiB,2);privateMiBEnd=[math]::Round($samples[-1].privateMiB,2);privateMiBPeak=[math]::Round(($samples.privateMiB | Measure-Object -Maximum).Maximum,2)
    handlesStart=$samples[0].handles;handlesEnd=$samples[-1].handles;threadsStart=$samples[0].threads;threadsEnd=$samples[-1].threads
    readBytes=($endIo.readBytes-$startIo.readBytes);readOperations=($endIo.reads-$startIo.reads);writeBytes=($endIo.writeBytes-$startIo.writeBytes)
    samples=$samples.ToArray()
}
$json=$result | ConvertTo-Json -Depth 5
if($Report){[IO.File]::WriteAllText($Report,$json,[Text.UTF8Encoding]::new($false))}
$result.Remove('samples');$result | ConvertTo-Json -Depth 3
