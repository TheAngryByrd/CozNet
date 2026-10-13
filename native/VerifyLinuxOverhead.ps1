param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-overhead'))

$ErrorActionPreference = 'Stop'
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
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $imageId uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux kernel.' }
$library = Join-Path $native 'libCozNet.Profiler.so'
$assembly = Join-Path $workload 'CozNet.LinuxPerf.dll'
$profilerHash = (Get-FileHash $library -Algorithm SHA256).Hash
$workloadHash = (Get-FileHash $assembly -Algorithm SHA256).Hash
$seed = 20261015
$random = [Random]::new($seed)
$stages = @('baseline', 'loaded', 'probes', 'callbacks', 'collection')
$order = [Collections.Generic.List[object]]::new()
$outcomes = [Collections.Generic.List[object]]::new()
$ratios = [Collections.Generic.List[object]]::new()
@{ seed = $seed; blocks = 8; image = $image; imageId = $imageId; kernel = $kernel;
    measurementMilliseconds = 2000; profilerSha256 = $profilerHash; workloadSha256 = $workloadHash;
    perfMapEnabledForAllStages = $true; controllerRuntime = [Environment]::Version.ToString() } |
    ConvertTo-Json | Set-Content (Join-Path $run 'environment.json')
for ($block = 0; $block -lt 8; $block++) {
    $cases = $stages.Clone()
    for ($i = $cases.Length - 1; $i -gt 0; $i--) {
        $j = $random.Next($i + 1)
        $cases[$i], $cases[$j] = $cases[$j], $cases[$i]
    }
    $costs = @{}
    foreach ($case in $cases) {
        $file = "block-$block-$case.json"
        $order.Add(@{ block = $block; stage = $case; file = $file })
        $order | ConvertTo-Json | Set-Content (Join-Path $run 'order.json')
        $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
            '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '--security-opt', 'seccomp=unconfined',
            '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
            '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1',
            '--mount', "type=bind,source=$workload,target=/work,readonly", '--mount', "type=bind,source=$run,target=/output")
        if ($case -ne 'baseline') {
            $target = if ($case -eq 'loaded') { 'instrumentation' } else { 'linux-perf' }
            $arguments += @('--mount', "type=bind,source=$native,target=/native,readonly", '--env', 'CORECLR_ENABLE_PROFILING=1',
                '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}',
                '--env', 'CORECLR_PROFILER_PATH=/native/libCozNet.Profiler.so', '--env', 'LD_LIBRARY_PATH=/native',
                '--env', "COZNET_PROBE_TARGET=$target", '--env', 'COZNET_PROBE_INTERVAL=1024',
                '--env', 'COZNET_IL_MAP_MODE=registered', '--env', 'COZNET_PROBE_PLACEMENT=branch')
        }
        & docker @arguments $imageId timeout 30s dotnet /work/CozNet.LinuxPerf.dll --overhead "/output/$file" $case 2000 *> (Join-Path $run "$file.log")
        $exitCode = $LASTEXITCODE
        $outcomes.Add(@{ block = $block; stage = $case; file = $file; exitCode = $exitCode })
        $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
        if ($exitCode -ne 0) { continue }
        $data = Get-Content (Join-Path $run $file) -Raw | ConvertFrom-Json
        if ($data.completedBatches -le 0 -or $data.experimentMilliseconds -le 0) { throw 'Invalid overhead progress or duration.' }
        $costs[$case] = $data.experimentMilliseconds / $data.completedBatches
    }
    if ($costs.Count -eq $stages.Length) {
        for ($i = 0; $i -lt $stages.Length; $i++) {
            $stage = $stages[$i]
            $previous = if ($i -eq 0) { $stage } else { $stages[$i - 1] }
            $ratios.Add(@{ block = $block; stage = $stage; millisecondsPerBatch = $costs[$stage];
                baselineIncrease = $costs[$stage] / $costs['baseline'] - 1;
                incrementalIncrease = $costs[$stage] / $costs[$previous] - 1 })
        }
    }
}
$summary = foreach ($stage in $stages) {
    $values = @($ratios | Where-Object { $_.stage -eq $stage })
    if ($values.Count -ne 8) { continue }
    $mean = ($values.incrementalIncrease | Measure-Object -Average).Average
    $variance = ($values | ForEach-Object { [Math]::Pow($_.incrementalIncrease - $mean, 2) } | Measure-Object -Sum).Sum / 7
    @{ stage = $stage; meanBaselineIncrease = ($values.baselineIncrease | Measure-Object -Average).Average;
        meanIncrementalIncrease = $mean; incrementalIntervalHalfWidth = 2.364624251 * [Math]::Sqrt($variance / 8) }
}
$unchanged = (Get-FileHash $library -Algorithm SHA256).Hash -eq $profilerHash -and
    (Get-FileHash $assembly -Algorithm SHA256).Hash -eq $workloadHash
$passed = @($outcomes | Where-Object { $_.exitCode -ne 0 }).Count -eq 0 -and $unchanged
@{ allCasesPassed = $passed; binariesUnchanged = $unchanged; outcomes = $outcomes.ToArray(); blocks = $ratios.ToArray();
    stages = @($summary); confidence = 0.95; studentTCritical = 2.364624251; predictionAccuracyEstablished = $false } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'summary.json')
if (-not $passed) { throw "Overhead verification failed. Evidence: $run" }
$run
