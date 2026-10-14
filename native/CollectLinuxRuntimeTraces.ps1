param(
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/runtime-suspension-comparison'),
    [ValidateRange(1, 10)][int]$Repetitions = 3,
    [switch]$TargetCpu
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$native = Join-Path $root 'artifacts/native-signal'
$tool = Join-Path $root 'lode/tmp/diagnostic-tools/.store/dotnet-trace/10.0.750501/dotnet-trace/10.0.750501/tools/net8.0/any'
if (-not (Test-Path -LiteralPath (Join-Path $tool 'dotnet-trace.dll'))) {
    throw 'Install dotnet-trace 10.0.750501 with --tool-path lode/tmp/diagnostic-tools --allow-roll-forward.'
}
$imageId = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $imageId uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux kernel.' }
$files = @('tests/CozNet.LinuxPerf/SampledSignalTrace.cs', 'tests/CozNet.LinuxPerf/SampledSignalTraceChecks.cs',
    'tests/CozNet.LinuxPerf/SampledSignalWorkload.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'tests/CozNet.LinuxPerf/ControllerCpuSnapshot.cs', 'native/AnalyzeControllerCpu.py', 'native/ControllerCpuChecks.py',
    'native/CollectLinuxRuntimeTraces.ps1', 'native/RuntimeTraceReader.cs', 'native/AnalyzeRuntimeSuspension.py',
    'native/RuntimeSuspensionChecks.py', 'native/ExportRuntimeTrace.ps1',
    'native/signal/Signal.LinuxX64.cpp', 'native/signal/Signal.h',
    'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll',
    'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.dll', 'artifacts/native-signal/libCozNet.Signal.so')
$files += @('tests/CozNet.LinuxPerf/TargetCpuSnapshot.cs', 'tests/CozNet.LinuxPerf/SignalInterventionBackend.cs', 'native/signal/TargetCpuChecks.cpp', 'native/signal/CMakeLists.txt')
$files += @('native/AnalyzeTargetCpu.py', 'native/TargetCpuChecks.py', 'native/CollectLinuxTargetCpu.ps1',
    'artifacts/native-signal/TargetCpuChecks')
$route = if ($TargetCpu) { '--sampled-signal-target-cpu' } else { '--sampled-signal-trace' }
$hashes = @{}
foreach ($file in $files) {
    $path = Join-Path $root $file
    $hashes[$file] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $snapshot = Join-Path $run "snapshot/$file"
    New-Item -ItemType Directory -Force (Split-Path $snapshot) | Out-Null
    Copy-Item -LiteralPath $path -Destination $snapshot
}
$seed = 20261019
$random = [Random]::new($seed)
$cases = @(for ($block = 1; $block -le $Repetitions; $block++) {
    $items = @(foreach ($gc in @('no-gc', 'gc')) {
        foreach ($percent in @(0, 50)) { @{ block=$block; gc=$gc; percent=$percent } }
    })
    for ($i = $items.Length - 1; $i -gt 0; $i--) {
        $j = $random.Next($i + 1)
        $items[$i], $items[$j] = $items[$j], $items[$i]
    }
    $items
})
@{ imageId=$imageId; kernel=$kernel; hashes=$hashes; order=$cases; seed=$seed;
   bufferMiB=64; watchdogSeconds=30; diagnosticOnly=$true; controllerCpuTiming=$true;
   collectorHash=(Get-FileHash (Join-Path $tool 'dotnet-trace.dll') -Algorithm SHA256).Hash } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'environment.json')
& docker run --rm --pull never --network none --cap-drop ALL --read-only `
    --mount "type=bind,source=$workload,target=/work,readonly" $imageId `
    timeout -k 2s 30s dotnet /work/CozNet.LinuxPerf.dll --verify-signal-trace *> (Join-Path $run 'marker-checks.log')
$markerExit = $LASTEXITCODE
$targetClockChecksPassed = $true
if ($TargetCpu) {
    foreach ($mode in @('enabled', 'disabled')) {
        & docker run --rm --pull never --network none --cap-drop ALL --read-only --env LD_LIBRARY_PATH=/native `
            --mount "type=bind,source=$native,target=/native,readonly" $imageId timeout -k 2s 30s /native/TargetCpuChecks $mode *> (Join-Path $run "target-cpu-$mode.log")
        $code = $LASTEXITCODE
        $targetClockChecksPassed = $targetClockChecksPassed -and $code -eq 0
        @{ mode=$mode; exitCode=$code } | ConvertTo-Json | Set-Content (Join-Path $run "target-cpu-$mode.json")
    }
    if (-not $targetClockChecksPassed) { throw "Target CPU contract checks failed. Evidence: $run" }
}
$outcomes = [Collections.Generic.List[object]]::new()
foreach ($case in $cases) {
    $name = "block-$($case.block)-$($case.gc)-$($case.percent)"
    $directory = Join-Path $run $name
    New-Item -ItemType Directory -Force $directory | Out-Null
    $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--security-opt', 'seccomp=unconfined', '--tmpfs', '/tmp:rw,noexec,nosuid,size=128m',
        '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
        '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1',
        '--env', 'LD_LIBRARY_PATH=/native', '--env', 'CORECLR_ENABLE_PROFILING=0',
        '--mount', "type=bind,source=$workload,target=/work,readonly",
        '--mount', "type=bind,source=$native,target=/native,readonly",
        '--mount', "type=bind,source=$tool,target=/tool,readonly",
        '--mount', "type=bind,source=$directory,target=/output", $imageId,
        'timeout', '-k', '2s', '30s', 'dotnet', '/tool/dotnet-trace.dll', 'collect',
        '--providers', 'Microsoft-Windows-DotNETRuntime:0x1:4,CozNet-SampledSignals:0x0:4',
        '--buffersize', '64', '--rundown', 'false', '--output', '/output/trace.nettrace', '--',
        'dotnet', '/work/CozNet.LinuxPerf.dll', $route, '/output/workload.json',
        "$($case.percent)", 'qualified-sample', 'warm', 'spin', $case.gc)
    $started = [DateTime]::UtcNow
    & docker @arguments *> (Join-Path $directory 'collect.log')
    $exitCode = $LASTEXITCODE
    @{ arguments=$arguments; startedUtc=$started; finishedUtc=[DateTime]::UtcNow; exitCode=$exitCode } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $directory 'capture.json')
    $recordExists = Test-Path -LiteralPath (Join-Path $directory 'workload.json')
    $traceExists = Test-Path -LiteralPath (Join-Path $directory 'trace.nettrace')
    $outcomes.Add(@{ name=$name; exitCode=$exitCode; workloadExists=$recordExists; traceExists=$traceExists })
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
    Write-Host "$name exited $exitCode. Evidence: $directory"
}
$unchanged = $true
foreach ($file in $files) {
    if ((Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash -ne $hashes[$file]) { $unchanged = $false }
}
$passed = $unchanged -and $markerExit -eq 0 -and @($outcomes | Where-Object {
    $_.exitCode -ne 0 -or -not $_.workloadExists -or -not $_.traceExists
}).Count -eq 0
@{ workloadsPassed=$passed; markerCheckExit=$markerExit; sourcesAndBinariesUnchanged=$unchanged;
   correlationVerified=$false; outcomes=$outcomes.ToArray() } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
if (-not $passed) { throw 'The traced workload comparison failed. Retain every outcome.' }
