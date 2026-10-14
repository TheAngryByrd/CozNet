param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/scheduler-checks/linux'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
Copy-Item -LiteralPath $workload -Destination (Join-Path $run 'workload') -Recurse
$workload = Join-Path $run 'workload'
$files = @('tests/CozNet.LinuxPerf/SchedulerRecordParser.cs', 'tests/CozNet.LinuxPerf/SchedulerRecordChecks.cs',
    'tests/CozNet.LinuxPerf/SampledSignalAbortChecks.cs', 'tests/CozNet.LinuxPerf/SampledSignalWorkload.cs',
    'tests/CozNet.LinuxPerf/Program.cs', 'tests/CozNet.LinuxPerf/SchedulerEventSession.cs',
    'tests/CozNet.LinuxPerf/SchedulerCostWorkload.cs', 'tests/CozNet.LinuxPerf/SchedulerCostWorkloadChecks.cs',
    'native/VerifyLinuxScheduler.ps1')
foreach ($file in $files) {
    $destination = Join-Path $run "snapshot/$file"
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination
}
$outcomes = @()
foreach ($mode in @('records', 'normal', 'finish-failure', 'creation-failure', 'capture-failure', 'cost-startup')) {
    [string[]]$arguments = if ($mode -eq 'records') { @('--verify-scheduler-records') }
    elseif ($mode -eq 'cost-startup') { @('--verify-scheduler-cost-startup', '/output/cost-startup') } else {
        @('--verify-scheduler-shutdown', "/output/$mode.json", $mode)
    }
    & docker run --rm --pull never --network none --cap-drop ALL --read-only --tmpfs '/tmp:rw,nosuid,size=128m' `
        --env DOTNET_TieredCompilation=0 --env DOTNET_ReadyToRun=0 --env CORECLR_ENABLE_PROFILING=0 `
        --mount "type=bind,source=$workload,target=/work,readonly" --mount "type=bind,source=$run,target=/output" `
        $image timeout -k 2s 15s dotnet /work/CozNet.LinuxPerf.dll @arguments *> (Join-Path $run "$mode.log")
    $outcomes += @{ mode=$mode; exitCode=$LASTEXITCODE }
}
@{ image=$image; outcomes=$outcomes; passed=(@($outcomes | Where-Object exitCode -ne 0).Count -eq 0) } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
if (@($outcomes | Where-Object exitCode -ne 0).Count -ne 0) { throw "Scheduler checks failed: $run" }
$run
