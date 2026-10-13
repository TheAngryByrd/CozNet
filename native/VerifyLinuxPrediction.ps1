param(
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-prediction'),
    [ValidateSet(500, 2000)][int]$MeasurementMilliseconds = 2000,
    [ValidateSet('manual', 'injected')][string]$ProbeMode = 'manual',
    [ValidateSet(1, 64, 256, 1024)][int]$ProbeInterval = 1
)

$ErrorActionPreference = 'Stop'
if ($ProbeMode -eq 'manual' -and $ProbeInterval -ne 1) { throw 'Manual probes require interval 1.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$native = Join-Path $root 'artifacts/native-linux/integrated'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
$image = 'mcr.microsoft.com/dotnet/sdk:10.0.100'
$imageId = & docker image inspect $image --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The local Linux SDK image is unavailable.' }
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $image uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux kernel.' }
$seed = 20261013
$random = [Random]::new($seed)
$order = [Collections.Generic.List[object]]::new()
$rows = [Collections.Generic.List[object]]::new()
@{ seed = $seed; blocks = 8; image = $image; imageId = $imageId; kernel = $kernel;
    measurementMilliseconds = $MeasurementMilliseconds; controllerRuntime = [Environment]::Version.ToString();
    probeMode = $ProbeMode; probeInterval = $ProbeInterval } |
    ConvertTo-Json | Set-Content (Join-Path $run 'environment.json')

for ($block = 0; $block -lt 8; $block++) {
    $cases = @('profile-zero', 'profile-half', 'physical-baseline', 'physical-optimized')
    for ($i = $cases.Length - 1; $i -gt 0; $i--) {
        $j = $random.Next($i + 1)
        $cases[$i], $cases[$j] = $cases[$j], $cases[$i]
    }
    $results = @{}
    foreach ($case in $cases) {
        $file = "block-$block-$case.json"
        $order.Add(@{ block = $block; case = $case; file = $file })
        $order | ConvertTo-Json | Set-Content (Join-Path $run 'order.json')
        $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL',
            '--read-only', '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '--security-opt', 'seccomp=unconfined',
            '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
            '--mount', "type=bind,source=$workload,target=/work,readonly",
            '--mount', "type=bind,source=$run,target=/output")
        if ($case.StartsWith('profile-')) {
            $arguments += @('--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1')
            $arguments += @('--env', "COZNET_PROBE_MODE=$ProbeMode", '--env', "COZNET_PROBE_INTERVAL=$ProbeInterval")
            if ($ProbeMode -eq 'injected') {
                $arguments += @('--mount', "type=bind,source=$native,target=/native,readonly",
                    '--env', 'CORECLR_ENABLE_PROFILING=1', '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}',
                    '--env', 'CORECLR_PROFILER_PATH=/native/libCozNet.Profiler.so', '--env', 'LD_LIBRARY_PATH=/native',
                    '--env', 'COZNET_PROBE_TARGET=linux-perf')
            }
            $speedup = if ($case -eq 'profile-zero') { '0' } else { '50' }
            $application = @("/output/$file", $speedup, 'linux-nanosleep', '10', "$MeasurementMilliseconds")
        } else {
            $mode = if ($case -eq 'physical-baseline') { 'baseline' } else { 'optimized' }
            $application = @('--physical', "/output/$file", $mode, "$MeasurementMilliseconds")
        }
        $arguments += @($image, 'timeout', '30s', 'dotnet', '/work/CozNet.LinuxPerf.dll') + $application
        & docker @arguments
        if ($LASTEXITCODE -ne 0) { throw "Prediction workload $case failed in block $block. Evidence: $run" }
        $results[$case] = Get-Content (Join-Path $run $file) -Raw | ConvertFrom-Json
    }
    $costs = @{}
    foreach ($case in $cases) {
        $data = $results[$case]
        if ($case.StartsWith('profile-')) {
            $phase = $data.observations[0]
            $duration = $phase.experimentMilliseconds - 1000.0 * $phase.intervention.live.globalDelayTicks / $phase.intervention.clockFrequency
        } else { $duration = $data.experimentMilliseconds }
        if ($duration -le 0 -or $data.completedBatches -le 0) { throw "Invalid progress or corrected duration in $case." }
        $costs[$case] = $duration / $data.completedBatches
    }
    $predicted = 1 - $costs['profile-half'] / $costs['profile-zero']
    $actual = 1 - $costs['physical-optimized'] / $costs['physical-baseline']
    $rows.Add(@{ block = $block; predictedReduction = $predicted; actualReduction = $actual;
        error = $predicted - $actual; profilingOverhead = $costs['profile-zero'] / $costs['physical-baseline'] - 1; costs = $costs })
}

$meanError = ($rows.error | Measure-Object -Average).Average
$variance = ($rows | ForEach-Object { [Math]::Pow($_.error - $meanError, 2) } | Measure-Object -Sum).Sum / 7
$critical = 2.364624251
$halfWidth = $critical * [Math]::Sqrt($variance / 8)
$meanActual = ($rows.actualReduction | Measure-Object -Average).Average
$passed = [Math]::Abs($meanError) + $halfWidth -le 0.02 -and $meanActual -gt 0
@{ seed = $seed; blocks = $rows; meanPrediction = ($rows.predictedReduction | Measure-Object -Average).Average;
    meanActual = $meanActual; meanError = $meanError; errorIntervalHalfWidth = $halfWidth;
    confidence = 0.95; studentTCritical = $critical; errorLimit = 0.02; passed = $passed;
    meanProfilingOverhead = ($rows.profilingOverhead | Measure-Object -Average).Average } |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run 'summary.json')
if (-not $passed) { throw "Prediction error interval exceeds two percentage points. Evidence: $run" }
$run
