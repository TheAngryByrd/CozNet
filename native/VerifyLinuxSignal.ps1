param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-signal'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$native = Join-Path $root 'artifacts/native-signal'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
$image = 'mcr.microsoft.com/dotnet/sdk:10.0.100'
$imageId = & docker image inspect $image --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$library = Join-Path $native 'libCozNet.Signal.so'
$assembly = Join-Path $workload 'CozNet.LinuxPerf.dll'
$libraryHash = (Get-FileHash $library -Algorithm SHA256).Hash
$assemblyHash = (Get-FileHash $assembly -Algorithm SHA256).Hash
$contractHash = (Get-FileHash (Join-Path $native 'SignalContractChecks') -Algorithm SHA256).Hash
$sourceFiles = @('native/signal/Signal.LinuxX64.cpp', 'native/signal/Signal.h', 'native/signal/ContractChecks.cpp',
    'native/signal/CMakeLists.txt', 'tests/CozNet.LinuxPerf/SignalInterventionBackend.cs',
    'tests/CozNet.LinuxPerf/SignalWorkload.cs', 'tests/CozNet.LinuxPerf/InstructionMap.cs', 'tests/CozNet.LinuxPerf/Program.cs')
$sourceHashes = @{}
foreach ($file in $sourceFiles) { $sourceHashes[$file] = (Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash }
$seed = 20261016
$random = [Random]::new($seed)
$cases = @('baseline:no-gc', 'zero:no-gc', 'delay:no-gc', 'baseline:gc', 'zero:gc', 'delay:gc')
for ($i = $cases.Length - 1; $i -gt 0; $i--) {
    $j = $random.Next($i + 1)
    $cases[$i], $cases[$j] = $cases[$j], $cases[$i]
}
@{ seed = $seed; imageId = $imageId; librarySha256 = $libraryHash; workloadSha256 = $assemblyHash;
    contractSha256 = $contractHash; sourceHashes = $sourceHashes;
    order = $cases; requestedDelayNanoseconds = 50000; handlerBoundNanoseconds = 5000000;
    acknowledgmentBoundMilliseconds = 100; gcCallBoundMilliseconds = 250; watchdogSeconds = 30;
    measurementMilliseconds = 2000; profilerEnabled = $false } | ConvertTo-Json | Set-Content (Join-Path $run 'environment.json')
Copy-Item -LiteralPath (Join-Path $native 'disassembly.txt') -Destination $run
Copy-Item -LiteralPath (Join-Path $native 'handler-disassembly.txt') -Destination $run
$outcomes = [Collections.Generic.List[object]]::new()
foreach ($case in @('occupied', 'blocked', 'lifetime')) {
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
    & docker @arguments $imageId timeout -k 2s 30s dotnet /work/CozNet.LinuxPerf.dll --signal "/output/$file" $mode $gc *> (Join-Path $run "$file.log")
    $exitCode = $LASTEXITCODE
    $outcomes.Add(@{ case = $case; file = $file; exitCode = $exitCode; recordExists = Test-Path -LiteralPath (Join-Path $run $file) })
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
}
$unchanged = (Get-FileHash $library -Algorithm SHA256).Hash -eq $libraryHash -and (Get-FileHash $assembly -Algorithm SHA256).Hash -eq $assemblyHash
$unchanged = $unchanged -and (Get-FileHash (Join-Path $native 'SignalContractChecks') -Algorithm SHA256).Hash -eq $contractHash
foreach ($file in $sourceFiles) {
    $unchanged = $unchanged -and (Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash -eq $sourceHashes[$file]
}
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; binariesUnchanged = $unchanged; outcomes = $outcomes.ToArray();
    generalClrSafetyEstablished = $false; predictionAccuracyEstablished = $false } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
if (-not $passed) { throw "Signal verification failed. Evidence: $run" }
$run
