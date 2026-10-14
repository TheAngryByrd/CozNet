param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-signal-transitions'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$native = Join-Path $root 'artifacts/native-signal'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
$imageId = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $imageId uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux kernel.' }
$hashes = @{}
foreach ($file in @('native/signal/Signal.LinuxX64.cpp', 'native/signal/Signal.h', 'native/signal/ContractChecks.cpp',
    'native/signal/Workload.LinuxX64.cpp', 'native/signal/CMakeLists.txt', 'tests/CozNet.LinuxPerf/SignalTransitionWorkload.cs',
    'tests/CozNet.LinuxPerf/SignalTransitionOperations.cs', 'tests/CozNet.LinuxPerf/SignalInterventionBackend.cs',
    'tests/CozNet.LinuxPerf/SignalPhaseTiming.cs', 'tests/CozNet.LinuxPerf/SignalPhaseTimingChecks.cs',
    'tests/CozNet.LinuxPerf/SignalTargetLifetime.cs', 'tests/CozNet.LinuxPerf/SignalTargetChecks.cs', 'tests/CozNet.LinuxPerf/SignalWorkload.cs',
    'tests/CozNet.LinuxPerf/InstructionMap.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'artifacts/native-signal/libCozNet.Signal.so', 'artifacts/native-signal/libCozNet.SignalWorkload.so',
    'artifacts/native-signal/SignalContractChecks', 'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll')) {
    $hashes[$file] = (Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash
}
$seed = 20261017
$random = [Random]::new($seed)
$cases = @('jit:no-gc', 'native-wait:no-gc', 'managed-wait:no-gc', 'transitions:no-gc',
    'jit:gc', 'native-wait:gc', 'managed-wait:gc', 'transitions:gc')
for ($i = $cases.Length - 1; $i -gt 0; $i--) {
    $j = $random.Next($i + 1)
    $cases[$i], $cases[$j] = $cases[$j], $cases[$i]
}
@{ seed = $seed; imageId = $imageId; kernel = $kernel; hashes = $hashes; order = $cases;
    requestedDelayNanoseconds = 50000; handlerBoundNanoseconds = 5000000; acknowledgmentBoundMilliseconds = 100;
    gcCallBoundMilliseconds = 250; watchdogSeconds = 30; measurementMilliseconds = 2000 } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'environment.json')
Copy-Item -LiteralPath (Join-Path $native 'handler-disassembly.txt') -Destination $run
$outcomes = [Collections.Generic.List[object]]::new()
& docker run --rm --pull never --network none --cap-drop ALL --read-only `
    --mount "type=bind,source=$workload,target=/work,readonly" $imageId timeout -k 2s 30s dotnet /work/CozNet.LinuxPerf.dll --verify-signal-targets *> (Join-Path $run 'target-lifetime.log')
$outcomes.Add(@{ case = 'target-lifetime'; exitCode = $LASTEXITCODE; recordExists = $true })
foreach ($case in @('occupied', 'blocked', 'lifetime', 'pending-shutdown', 'exit-with-pending')) {
    & docker run --rm --pull never --network none --cap-drop ALL --read-only --env LD_LIBRARY_PATH=/native `
        --mount "type=bind,source=$native,target=/native,readonly" $imageId timeout -k 2s 30s /native/SignalContractChecks $case *> (Join-Path $run "$case.log")
    $outcomes.Add(@{ case = $case; exitCode = $LASTEXITCODE; recordExists = $true })
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
}
foreach ($case in $cases) {
    $mode, $gc = $case.Split(':')
    $file = "$mode-$gc.json"
    $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '--env', 'DOTNET_TieredCompilation=0',
        '--env', 'DOTNET_ReadyToRun=0', '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1',
        '--env', 'LD_LIBRARY_PATH=/native', '--env', 'CORECLR_ENABLE_PROFILING=0',
        '--mount', "type=bind,source=$workload,target=/work,readonly",
        '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$run,target=/output")
    & docker @arguments $imageId timeout -k 2s 30s dotnet /work/CozNet.LinuxPerf.dll --signal-transition "/output/$file" $mode $gc *> (Join-Path $run "$file.log")
    $outcomes.Add(@{ case = $case; file = $file; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run $file) })
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
}
$unchanged = $true
foreach ($file in $hashes.Keys) { $unchanged = $unchanged -and (Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash -eq $hashes[$file] }
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; sourcesAndBinariesUnchanged = $unchanged; outcomes = $outcomes.ToArray();
    generalClrSafetyEstablished = $false; predictionAccuracyEstablished = $false } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
if (-not $passed) { throw "Signal transition verification failed. Evidence: $run" }
$run
