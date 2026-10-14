param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-sampled-signals'))
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
foreach ($file in @('src/CozNet/SignalAccounting.fs', 'src/CozNet/DelayLedger.fs', 'src/CozNet/SampleCredits.fs',
    'tests/CozNet.LinuxPerf/SampledSignalWorkload.cs', 'tests/CozNet.LinuxPerf/SignalInterventionBackend.cs',
    'tests/CozNet.LinuxPerf/SignalTargetLifetime.cs', 'tests/CozNet.LinuxPerf/LinuxCpuEventSession.cs',
    'tests/CozNet.LinuxPerf/InstructionMap.cs', 'tests/CozNet.LinuxPerf/CpuSample.cs',
    'tests/CozNet.LinuxPerf/ProbeCadence.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'native/signal/Signal.LinuxX64.cpp', 'native/signal/Signal.h',
    'native/VerifyLinuxSampledSignals.ps1', 'artifacts/native-signal/libCozNet.Signal.so',
    'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll',
    'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.dll')) {
    $hashes[$file] = (Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash
}
@{ imageId = $imageId; kernel = $kernel; hashes = $hashes; order = @(0, 50);
    debtBoundNanoseconds = 2000000; measurementMilliseconds = 2000; watchdogSeconds = 30;
    seccomp = 'unconfined'; forcedGc = $false } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'environment.json')
$outcomes = [Collections.Generic.List[object]]::new()
foreach ($percent in @(0, 50)) {
    $file = "speedup-$percent.json"
    $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--security-opt', 'seccomp=unconfined', '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m',
        '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
        '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1',
        '--env', 'LD_LIBRARY_PATH=/native', '--env', 'CORECLR_ENABLE_PROFILING=0',
        '--mount', "type=bind,source=$workload,target=/work,readonly",
        '--mount', "type=bind,source=$native,target=/native,readonly",
        '--mount', "type=bind,source=$run,target=/output")
    & docker @arguments $imageId timeout -k 2s 30s dotnet /work/CozNet.LinuxPerf.dll --sampled-signal "/output/$file" "$percent" *> (Join-Path $run "$file.log")
    $outcomes.Add(@{ percent = $percent; file = $file; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run $file) })
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
}
$unchanged = $true
foreach ($file in $hashes.Keys) {
    $unchanged = $unchanged -and (Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash -eq $hashes[$file]
}
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; sourcesAndBinariesUnchanged = $unchanged; outcomes = $outcomes.ToArray();
    predictionAccuracyEstablished = $false; generalClrSafetyEstablished = $false } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'summary.json')
Write-Host "Sampled signal evidence: $run"
if (-not $passed) { throw 'The sampled signal capacity experiment failed. Retain its measurements.' }
