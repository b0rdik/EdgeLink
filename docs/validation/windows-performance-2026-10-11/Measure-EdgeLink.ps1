param(
    [Parameter(Mandatory=$true)][int]$TargetProcessId,
    [Parameter(Mandatory=$true)][string]$Scenario,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [int]$Samples=30
)
$ErrorActionPreference='Stop'
$target=Get-Process -Id $TargetProcessId
if ($target.ProcessName -ne 'EdgeLink') { throw 'Expected an EdgeLink process' }
$logicalProcessors=[Environment]::ProcessorCount
$version=(Get-Item -LiteralPath $target.Path).VersionInfo.ProductVersion
$clock=[Diagnostics.Stopwatch]::StartNew()
$previousTime=$clock.Elapsed.TotalSeconds
$previousCpu=$target.TotalProcessorTime.TotalSeconds
$rows=@()
for ($sample=0; $sample -lt $Samples; $sample++) {
    Start-Sleep -Milliseconds 1000
    $target.Refresh()
    if ($target.HasExited) { throw 'EdgeLink exited during sampling' }
    $now=$clock.Elapsed.TotalSeconds
    $cpu=$target.TotalProcessorTime.TotalSeconds
    $corePercent=100*($cpu-$previousCpu)/($now-$previousTime)
    $rows += [pscustomobject]@{
        elapsedSeconds=[Math]::Round($now,4)
        intervalSeconds=[Math]::Round($now-$previousTime,4)
        cpuSeconds=$cpu-$previousCpu
        cpuPercentOfMachine=$corePercent/$logicalProcessors
        cpuPercentOfOneLogicalProcessor=$corePercent
        workingSetMiB=$target.WorkingSet64/1MB
        privateCommittedMiB=$target.PrivateMemorySize64/1MB
        threads=$target.Threads.Count
        handles=$target.HandleCount
    }
    $previousTime=$now; $previousCpu=$cpu
}
$duration=($rows | Measure-Object intervalSeconds -Sum).Sum
$cpuTotal=($rows | Measure-Object cpuSeconds -Sum).Sum
$sorted=@($rows.cpuPercentOfMachine | Sort-Object)
$result=[pscustomobject]@{
    scenario=$Scenario
    productVersion=$version
    startedUtc=[DateTime]::UtcNow.AddSeconds(-$clock.Elapsed.TotalSeconds).ToString('o')
    logicalProcessors=$logicalProcessors
    durationSeconds=[Math]::Round($duration,3)
    sampleCount=$Samples
    summary=[pscustomobject]@{
        averageCpuPercentOfMachine=100*$cpuTotal/$duration/$logicalProcessors
        averageCpuPercentOfOneLogicalProcessor=100*$cpuTotal/$duration
        p95SampleCpuPercentOfMachine=$sorted[[Math]::Ceiling(.95*$Samples)-1]
        maxSampleCpuPercentOfMachine=($sorted | Measure-Object -Maximum).Maximum
        meanWorkingSetMiB=($rows | Measure-Object workingSetMiB -Average).Average
        meanPrivateCommittedMiB=($rows | Measure-Object privateCommittedMiB -Average).Average
        workingSetRangeMiB=@(($rows | Measure-Object workingSetMiB -Minimum).Minimum,($rows | Measure-Object workingSetMiB -Maximum).Maximum)
        privateCommittedRangeMiB=@(($rows | Measure-Object privateCommittedMiB -Minimum).Minimum,($rows | Measure-Object privateCommittedMiB -Maximum).Maximum)
        threadsRange=@(($rows | Measure-Object threads -Minimum).Minimum,($rows | Measure-Object threads -Maximum).Maximum)
        handlesRange=@(($rows | Measure-Object handles -Minimum).Minimum,($rows | Measure-Object handles -Maximum).Maximum)
    }
    samples=$rows
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputFile -Encoding utf8
$result.summary | ConvertTo-Json -Depth 3
