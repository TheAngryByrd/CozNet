param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/scheduler-capture'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SchedulerEvidence.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$native = Join-Path $root 'artifacts/native-signal'
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $image uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux kernel.' }
$files = @('tests/CozNet.LinuxPerf/SchedulerEventSession.cs', 'tests/CozNet.LinuxPerf/LinuxSchedulerEventSession.cs',
    'tests/CozNet.LinuxPerf/SchedulerRecordParser.cs',
    'tests/CozNet.LinuxPerf/SampledSignalWorkload.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'tests/CozNet.LinuxPerf/SignalTargetLifetime.cs', 'native/CollectLinuxScheduler.ps1',
    'native/SchedulerEvidence.ps1', 'native/AnalyzeScheduler.py',
    'artifacts/native-signal/libCozNet.Signal.so')
$hashes = @{}
foreach ($file in $files) {
    $path = Join-Path $root $file
    $hashes[$file] = (Get-FileHash $path -Algorithm SHA256).Hash
    $destination = Join-Path $run "snapshot/$file"
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $path -Destination $destination
}
Copy-Item -LiteralPath $workload -Destination (Join-Path $run 'workload') -Recurse
$workload = Join-Path $run 'workload'
$native = Join-Path $run 'snapshot/artifacts/native-signal'
$snapshotHashes = Get-SchedulerSnapshotHashes $run
$snapshotHashes | ConvertTo-Json | Set-Content (Join-Path $run 'snapshot-hashes.json')
$outcomes = @()
$cases = @(@{percent=0;gc='no-gc'}, @{percent=50;gc='gc'}, @{percent=0;gc='gc'}, @{percent=50;gc='no-gc'})
@{ image=$image; kernel=$kernel; sourceHashes=$hashes; order=$cases; watchdogSeconds=30; measuresObserverCost=$false } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'environment.json')
foreach ($case in $cases) {
    $name = "$($case.gc)-$($case.percent)"
    $directory = Join-Path $run $name
    New-Item -ItemType Directory $directory | Out-Null
    $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--security-opt', 'seccomp=unconfined', '--tmpfs', '/tmp:rw,noexec,nosuid,size=128m',
        '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0', '--env', 'DOTNET_PerfMapEnabled=3',
        '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--env', 'CORECLR_ENABLE_PROFILING=0', '--env', 'LD_LIBRARY_PATH=/native',
        '--mount', "type=bind,source=$workload,target=/work,readonly", '--mount', "type=bind,source=$native,target=/native,readonly",
        '--mount', "type=bind,source=$directory,target=/output", $image, 'timeout', '-k', '2s', '30s',
        'dotnet', '/work/CozNet.LinuxPerf.dll', '--sampled-signal-scheduler', '/output/workload.json',
        "$($case.percent)", 'qualified-sample', 'warm', 'spin', $case.gc)
    & docker @arguments *> (Join-Path $directory 'capture.log')
    $outcomes += @{ name=$name; exitCode=$LASTEXITCODE; workloadExists=(Test-Path (Join-Path $directory 'workload.json')); arguments=$arguments }
    $outcomes | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'outcomes.json')
}
Complete-SchedulerEvidence -Run $run -Hashes $snapshotHashes -Outcomes $outcomes `
    -Records @($outcomes | ForEach-Object { "$($_.name)/workload.json" }) -Analyzer 'AnalyzeScheduler.py'
$run
