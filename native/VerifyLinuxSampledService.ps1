param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-sampled-service'), [switch]$CapturePhases,
    [switch]$CaptureJit, [switch]$InitializeManagedPaths, [switch]$CaptureSubmission, [switch]$CaptureScheduler, [switch]$CompareOwnerPolling)
$ErrorActionPreference = 'Stop'
if ($CompareOwnerPolling) { $CaptureScheduler = $true }
if ($CaptureScheduler) { $CaptureSubmission = $true }
if ($CaptureSubmission) { $InitializeManagedPaths = $true }
if ($CaptureJit -and $InitializeManagedPaths) { throw 'Select the JIT trace or the initialized phase comparison.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$native = Join-Path $run 'native'
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$tool = Join-Path $root 'lode/tmp/diagnostic-tools/.store/dotnet-trace/10.0.750501/dotnet-trace/10.0.750501/tools/net8.0/any'
if ($CaptureJit -and -not (Test-Path -LiteralPath (Join-Path $tool 'dotnet-trace.dll'))) {
    throw 'Install dotnet-trace 10.0.750501 with --tool-path lode/tmp/diagnostic-tools --allow-roll-forward.'
}
New-Item -ItemType Directory -Force $native | Out-Null
$compilerImage = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$runtimeImage = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$hashes = @{}
if ($CaptureScheduler) {
    foreach ($name in @('AnalyzeServiceSnapshotLag.py', 'AnalyzeScheduler.py', 'ServiceSnapshotLagChecks.py', 'SchedulerChecks.py')) {
        $source = Join-Path $root "native/$name"
        $hashes["native/$name"] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        Copy-Item -LiteralPath $source -Destination (Join-Path $run $name)
    }
}
foreach ($directory in @('native/service', 'native/signal', 'tests/CozNet.LinuxPerf', 'src/CozNet')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $directory) -File) {
        if ($file.Extension -in @('.cs', '.fs', '.fsi', '.cpp', '.h', '.txt', '.csproj', '.fsproj')) {
            $hashes[[IO.Path]::GetRelativePath($root, $file.FullName)] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
}
foreach ($file in @('native/VerifyLinuxSampledService.ps1', 'native/RuntimeTraceReader.cs', 'native/ExportRuntimeTrace.ps1', 'build/build.fs',
    'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll', 'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.dll')) {
    $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash
}
$compiler = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--tmpfs', '/tmp:rw,nosuid,size=64m',
    '--mount', "type=bind,source=$root,target=/source,readonly", '--mount', "type=bind,source=$native,target=/build", $compilerImage)
& docker @compiler cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Service configuration failed. See $run/configure.log." }
& docker @compiler cmake --build /build --target ServiceApiChecks ServiceOwnerChecks *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Service build failed. See $run/build.log." }
$nativeHashes = @{}
foreach ($file in @('libCozNet.Service.so', 'signal/libCozNet.Signal.so')) {
    $nativeHashes[$file] = (Get-FileHash -LiteralPath (Join-Path $native $file) -Algorithm SHA256).Hash
}
$cases = @(0, 50)
$policies = if ($CompareOwnerPolling) { @('yield', 'poll-64') } else { @('yield') }
@{ sourcesAndManagedBinaries = $hashes; nativeBinaries = $nativeHashes; compilerImage = $compilerImage; runtimeImage = $runtimeImage;
    cases = $cases; cadence = 'qualified-sample'; startup = 'warm'; waitMode = 'spin'; gcMode = 'no-gc';
    capturePhases = [bool]($CapturePhases -or $CaptureJit -or $InitializeManagedPaths); captureJit = [bool]$CaptureJit;
    initializeManagedPaths = [bool]$InitializeManagedPaths; captureSubmission = [bool]$CaptureSubmission; captureScheduler = [bool]$CaptureScheduler;
    captureServiceOwnerScheduling = [bool]$CaptureScheduler; ownerPollingPolicies = $policies;
    caseOrder = 'policy-major: yield then poll-64; zero then 50 percent';
    debtBoundNanoseconds = 2000000; handlerBoundNanoseconds = 5000000; receiptBoundNanoseconds = 100000000;
    childRequestBoundNanoseconds = 50000; measurementMilliseconds = 2000; watchdogSeconds = 30 } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$outcomes = [Collections.Generic.List[object]]::new()
if ($CaptureScheduler) {
    foreach ($check in @('ServiceSnapshotLagChecks.py', 'SchedulerChecks.py')) {
        & python (Join-Path $run $check) *> (Join-Path $run "$check.log")
        $outcomes.Add(@{ name = $check; exitCode = $LASTEXITCODE; recordExists = $true })
    }
}
& docker @compiler ctest --test-dir /build -R '^service-' --output-on-failure *> (Join-Path $run 'native-checks.log')
$outcomes.Add(@{ name = 'native'; exitCode = $LASTEXITCODE; recordExists = $true })
$runtime = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--security-opt', 'seccomp=unconfined',
    '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
    '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1', '--env', 'CORECLR_ENABLE_PROFILING=0',
    '--env', 'LD_LIBRARY_PATH=/native:/native/signal', '--mount', "type=bind,source=$workload,target=/work,readonly",
    '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$run,target=/output")
if ($CaptureJit) { $runtime += @('--mount', "type=bind,source=$tool,target=/tool,readonly") }
$runtime += @($runtimeImage, 'timeout', '-k', '2s', '30s')
& docker @runtime dotnet /work/CozNet.LinuxPerf.dll --verify-service-accounting *> (Join-Path $run 'accounting-checks.log')
$outcomes.Add(@{ name = 'accounting'; exitCode = $LASTEXITCODE; recordExists = $true })
if ($CaptureScheduler) {
    & docker @runtime dotnet /work/CozNet.LinuxPerf.dll --verify-scheduler-records *> (Join-Path $run 'scheduler-checks.log')
    $outcomes.Add(@{ name = 'scheduler'; exitCode = $LASTEXITCODE; recordExists = $true })
    foreach ($mode in @('normal', 'finish-failure')) {
        $file = "owner-scheduler-$mode.json"
        & docker @runtime dotnet /work/CozNet.LinuxPerf.dll --verify-owner-scheduler-shutdown "/output/$file" $mode *> (Join-Path $run "$file.log")
        $outcomes.Add(@{ name = "owner-scheduler-$mode"; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run $file) })
    }
}
if ($InitializeManagedPaths) {
    foreach ($percent in $cases) {
        $file = "initialization-check-$percent.json"
        & docker @runtime dotnet /work/CozNet.LinuxPerf.dll --verify-service-initialization "/output/$file" $percent *> (Join-Path $run "$file.log")
        $outcomes.Add(@{ name = "initialization-$percent"; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run $file) })
    }
}
foreach ($policy in $policies) {
    foreach ($percent in $cases) {
        $caseName = if ($CompareOwnerPolling) { "$policy-speedup-$percent" } else { "speedup-$percent" }
        $file = "$caseName.json"
        $mode = if ($CaptureScheduler) { '--sampled-service-scheduler' } elseif ($CaptureSubmission) { '--sampled-service-submission' } elseif ($CaptureJit) { '--sampled-service-jit' } elseif ($InitializeManagedPaths) { '--sampled-service-initialized' }
            elseif ($CapturePhases) { '--sampled-service-phases' } else { '--sampled-service' }
        $launch = @('dotnet', '/work/CozNet.LinuxPerf.dll')
        if ($CaptureJit) {
            $launch = @('dotnet', '/tool/dotnet-trace.dll', 'collect', '--providers',
                'Microsoft-Windows-DotNETRuntime:0x11:5,CozNet-SampledSignals:0x0:4', '--buffersize', '64', '--rundown', 'false',
                '--output', "/output/speedup-$percent.nettrace", '--') + $launch
        }
        & docker @runtime @launch $mode "/output/$file" $percent qualified-sample warm spin no-gc $policy *> (Join-Path $run "$file.log")
        $outcomes.Add(@{ name = $caseName; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run $file) })
        if ($CaptureScheduler) {
            $analysisName = if ($CompareOwnerPolling) { "$policy-scheduler-analysis-$percent" } else { "scheduler-analysis-$percent" }
            $analysis = Join-Path $run "$analysisName.json"
            & python (Join-Path $run 'AnalyzeServiceSnapshotLag.py') (Join-Path $run $file) $analysis --scheduler *> (Join-Path $run "$analysisName.log")
            $outcomes.Add(@{ name = $analysisName; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath $analysis })
        }
        if ($CaptureJit) {
            $tracePath = Join-Path $run "speedup-$percent.nettrace"
            $outcomes.Add(@{ name = "trace-$percent"; exitCode = 0; recordExists = Test-Path -LiteralPath $tracePath })
        }
        $outcomes | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $run 'outcomes.json')
    }
}
$unchanged = $true
if ($CaptureScheduler) {
    foreach ($name in @('AnalyzeServiceSnapshotLag.py', 'AnalyzeScheduler.py', 'ServiceSnapshotLagChecks.py', 'SchedulerChecks.py')) {
        $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $run $name) -Algorithm SHA256).Hash -eq $hashes["native/$name"]
    }
}
foreach ($file in $hashes.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash -eq $hashes[$file] }
foreach ($file in $nativeHashes.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $native $file) -Algorithm SHA256).Hash -eq $nativeHashes[$file] }
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; outcomes = $outcomes.ToArray(); predictionAccuracyEstablished = $false;
    generalClrSafetyEstablished = $false } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'The native sampled service experiment failed. Retain this run.' }
