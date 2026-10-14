param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/scheduler-cost'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SchedulerEvidence.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
Copy-Item -LiteralPath $workload -Destination (Join-Path $run 'workload') -Recurse
$workload = Join-Path $run 'workload'
$files = @('tests/CozNet.LinuxPerf/SchedulerEventSession.cs', 'tests/CozNet.LinuxPerf/LinuxSchedulerEventSession.cs',
    'tests/CozNet.LinuxPerf/SchedulerRecordParser.cs', 'tests/CozNet.LinuxPerf/SchedulerCostWorkload.cs',
    'tests/CozNet.LinuxPerf/Program.cs', 'native/MeasureLinuxSchedulerCost.ps1',
    'native/SchedulerEvidence.ps1', 'native/AnalyzeScheduler.py', 'native/AnalyzeSchedulerCost.py')
foreach ($file in $files) {
    $destination = Join-Path $run "snapshot/$file"
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination
}
$snapshotHashes = Get-SchedulerSnapshotHashes $run
$snapshotHashes | ConvertTo-Json | Set-Content (Join-Path $run 'snapshot-hashes.json')
$random = [Random]::new(20261021)
$cases = @(for ($block = 1; $block -le 8; $block++) {
    $modes = if ($random.Next(2) -eq 0) { @('disabled', 'enabled') } else { @('enabled', 'disabled') }
    foreach ($mode in $modes) { @{ block=$block; mode=$mode } }
})
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $image uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the kernel.' }
@{ image=$image; kernel=$kernel; seed=20261021; order=$cases; watchdogSeconds=30; iterationsPerThread=300000; threads=3 } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'environment.json')
$outcomes = @()
foreach ($case in $cases) {
    $name = "block-$($case.block)-$($case.mode)"
    & docker run --rm --pull never --network none --cap-drop ALL --read-only --security-opt seccomp=unconfined `
        --tmpfs '/tmp:rw,nosuid,size=128m' --env DOTNET_TieredCompilation=0 --env DOTNET_ReadyToRun=0 --env CORECLR_ENABLE_PROFILING=0 `
        --mount "type=bind,source=$workload,target=/work,readonly" --mount "type=bind,source=$run,target=/output" `
        $image timeout -k 2s 30s dotnet /work/CozNet.LinuxPerf.dll --scheduler-cost "/output/$name.json" $case.mode *> (Join-Path $run "$name.log")
    $outcomes += @{ name=$name; exitCode=$LASTEXITCODE }
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
}
Complete-SchedulerEvidence -Run $run -Hashes $snapshotHashes -Outcomes $outcomes `
    -Records @($outcomes | ForEach-Object { "$($_.name).json" }) -Analyzer 'AnalyzeSchedulerCost.py'
$run
