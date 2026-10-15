param([string]$EvidenceDirectory, [switch]$ManagedWorkload, [switch]$ForceGc, [switch]$TransitionMatrix, [switch]$SampledTimer, [switch]$ControllerPhases, [switch]$InitializeManagedPaths, [switch]$InitializeNativeTransport, [switch]$ShortInitialInterval, [switch]$SampledGc, [switch]$SchedulerCapture, [switch]$SchedulerCost)
$ErrorActionPreference = 'Stop'
if ($SchedulerCost -and (-not $SampledTimer -or -not $SampledGc -or $SchedulerCapture)) { throw 'SchedulerCost requires sampled GC capacity mode without SchedulerCapture.' }
if ($SchedulerCapture -and (-not $SampledTimer -or -not $SampledGc)) { throw 'SchedulerCapture requires SampledTimer and SampledGc.' }
if ($SampledGc -and (-not $InitializeNativeTransport -or $ControllerPhases -or -not $ShortInitialInterval)) { throw 'SampledGc requires prepared short-interval capacity mode.' }
if ($ControllerPhases -and -not $SampledTimer) { throw 'ControllerPhases requires SampledTimer.' }
if ($InitializeManagedPaths -and (-not $SampledTimer -or (-not $ControllerPhases -and -not $InitializeNativeTransport))) { throw 'InitializeManagedPaths requires SampledTimer and either ControllerPhases or InitializeNativeTransport.' }
if ($InitializeNativeTransport -and -not $ControllerPhases -and -not $ShortInitialInterval) { throw 'Capacity mode currently requires ShortInitialInterval.' }
if ($InitializeNativeTransport -and -not $InitializeManagedPaths) { throw 'InitializeNativeTransport requires InitializeManagedPaths.' }
if ($ShortInitialInterval -and -not ($InitializeNativeTransport -or $TransitionMatrix)) { throw 'ShortInitialInterval requires InitializeNativeTransport or TransitionMatrix.' }
if ($SampledTimer -and ($ManagedWorkload -or $ForceGc -or $TransitionMatrix)) { throw 'SampledTimer requires its own run.' }
if ($TransitionMatrix -and ($ManagedWorkload -or $ForceGc)) { throw 'TransitionMatrix cannot combine with ManagedWorkload or ForceGc.' }
if ($ForceGc -and -not $ManagedWorkload) { throw 'ForceGc requires ManagedWorkload.' }
if (-not $EvidenceDirectory) {
    $directoryName = if ($SampledTimer) { 'timer-sampled' } elseif ($TransitionMatrix) { 'timer-transitions' } elseif ($ForceGc) { 'timer-managed-gc' } elseif ($ManagedWorkload) { 'timer-managed' } else { 'timer-interop' }
    $EvidenceDirectory = Join-Path $PSScriptRoot "../artifacts/$directoryName"
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$native = Join-Path $run 'native'
New-Item -ItemType Directory -Force $native | Out-Null
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$compilerImage = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$runtimeImage = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$compiler = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly",
    '--mount', "type=bind,source=$native,target=/build", $compilerImage)
$sources = @('native/service/TimerApi.h', 'native/service/TimerApi.Linux.cpp', 'native/service/LeaseMailbox.h',
    'native/service/TargetLease.h', 'native/service/TargetLease.cpp', 'native/service/CMakeLists.txt',
    'tests/CozNet.LinuxPerf/NativeTimerClient.cs', 'tests/CozNet.LinuxPerf/TimerInteropChecks.cs',
    'tests/CozNet.LinuxPerf/TimerAccountingAdapter.cs', 'tests/CozNet.LinuxPerf/TimerAccountingAdapterChecks.cs',
    'tests/CozNet.LinuxPerf/TimerManagedWorkload.cs', 'tests/CozNet.LinuxPerf/InstructionMap.cs', 'tests/CozNet.LinuxPerf/OverheadWorkload.cs',
    'tests/CozNet.LinuxPerf/TimerSelectedWorker.cs', 'src/CozNet/SignalAccounting.fs', 'src/CozNet/SignalService.fs',
    'tests/CozNet.LinuxPerf/TimerWorkerLifetime.cs', 'tests/CozNet.LinuxPerf/TimerWorkerLifetimeChecks.cs',
    'src/CozNet/DelayLedger.fs', 'src/CozNet/SampleCredits.fs',
    'tests/CozNet.LinuxPerf/TimerWorkloadPlan.cs', 'tests/CozNet.LinuxPerf/TimerWorkloadPlanChecks.cs',
    'tests/CozNet.LinuxPerf/TimerGcReach.cs', 'tests/CozNet.LinuxPerf/TimerGcReachChecks.cs',
    'tests/CozNet.LinuxPerf/SampledGcStress.cs', 'tests/CozNet.LinuxPerf/SampledGcStressChecks.cs',
    'tests/CozNet.LinuxPerf/TargetLeaseReceipts.cs', 'tests/CozNet.LinuxPerf/TargetLeaseReceiptsChecks.cs',
    'tests/CozNet.LinuxPerf/ServiceReceiptTiming.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj', 'native/VerifyTimerInterop.ps1', 'build/build.fs')
$sources += @('tests/CozNet.LinuxPerf/SchedulerEventSession.cs', 'tests/CozNet.LinuxPerf/LinuxSchedulerEventSession.cs', 'tests/CozNet.LinuxPerf/SchedulerRecordParser.cs')
$sources += @('tests/CozNet.LinuxPerf/TimerSchedulerCaptureGroup.cs', 'tests/CozNet.LinuxPerf/TimerSchedulerCaptureGroupChecks.cs')
$sources += 'tests/CozNet.LinuxPerf/TimerDebtLimit.cs'
$sources += @('tests/CozNet.LinuxPerf/TimerSessionCleanup.cs', 'tests/CozNet.LinuxPerf/TimerSessionCleanupChecks.cs')
$sources += @('tests/CozNet.LinuxPerf/MeasurementWindowCapture.cs', 'tests/CozNet.LinuxPerf/MeasurementWindowCaptureChecks.cs')
$sources += @('tests/CozNet.LinuxPerf/MatchedCpuWorkers.cs', 'tests/CozNet.LinuxPerf/MatchedPhysicalWorkload.cs')
$sources += 'tests/CozNet.LinuxPerf/MatchedCpuWorkersChecks.cs'
$hashes = @{}
$sources += 'native/service/TimerJournalSnapshot.h'
$sources += @('tests/CozNet.LinuxPerf/ITimerTransport.cs', 'tests/CozNet.LinuxPerf/TimerServiceController.cs',
    'tests/CozNet.LinuxPerf/TimerServiceControllerChecks.cs', 'tests/CozNet.LinuxPerf/SampledTimerWorkload.cs',
    'tests/CozNet.LinuxPerf/LinuxCpuEventSession.cs', 'tests/CozNet.LinuxPerf/CpuSample.cs',
    'tests/CozNet.LinuxPerf/SelectedSignalCadence.cs', 'tests/CozNet.LinuxPerf/ControllerPhaseCapture.cs',
    'tests/CozNet.LinuxPerf/ControllerCpuSnapshot.cs', 'tests/CozNet.LinuxPerf/TimerPathPreparation.cs',
    'tests/CozNet.LinuxPerf/ServicePathPreparation.cs', 'tests/CozNet.LinuxPerf/TimerTransportPreparation.cs',
    'tests/CozNet.LinuxPerf/ControllerPhaseCaptureChecks.cs', 'tests/CozNet.LinuxPerf/TimerInitialInterval.cs',
    'tests/CozNet.LinuxPerf/TimerInitialIntervalChecks.cs')
if ($TransitionMatrix) {
    $sources += @('tests/CozNet.LinuxPerf/SignalTransitionWorkload.cs', 'tests/CozNet.LinuxPerf/SignalTransitionOperations.cs',
        'native/signal/CMakeLists.txt', 'native/signal/Workload.LinuxX64.cpp')
}
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash }
& docker @compiler uname -a *> (Join-Path $run 'kernel.txt')
if ($LASTEXITCODE -ne 0) { throw 'Kernel identity capture failed.' }
& docker @compiler cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Timer interop configuration failed. See $run/configure.log." }
$targets = @('CozNet.Timer')
if ($TransitionMatrix) { $targets += 'CozNet.SignalWorkload' }
& docker @compiler cmake --build /build --target @targets *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Timer interop compilation failed. See $run/build.log." }
$binaryPaths = @((Join-Path $native 'libCozNet.Timer.so'), (Join-Path $workload 'CozNet.LinuxPerf.dll'), (Join-Path $workload 'CozNet.dll'))
$binaries = @{}
if ($TransitionMatrix) { $binaryPaths += Join-Path $native 'signal/libCozNet.SignalWorkload.so' }
foreach ($path in $binaryPaths) { $binaries[[IO.Path]::GetFileName($path)] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
@{ sources = $hashes; binaries = $binaries; compilerImage = $compilerImage; runtimeImage = $runtimeImage;
    receiptBoundNanoseconds = 100000000; delayedReadMilliseconds = 150; watchdogSeconds = 20;
    managedWorkload = [bool]$ManagedWorkload; forceGc = [bool]$ForceGc; gcCallBoundMilliseconds = 250;
    transitionMatrix = [bool]$TransitionMatrix;
    fixedCreditSteps = $(if ($TransitionMatrix) { 128 } else { 16 }); requestedChildBoundNanoseconds = 50000;
    handlerBoundNanoseconds = 5000000; leaseBudgetBoundNanoseconds = 2000000; sampledTimer = [bool]$SampledTimer; observedDebtBoundNanoseconds = 2000000;
    controllerPhases = [bool]$ControllerPhases; initializeManagedPaths = [bool]$InitializeManagedPaths;
    initializeNativeTransport = [bool]$InitializeNativeTransport; initialIntervalNanoseconds = $(if ($ShortInitialInterval) { 10000 } else { 1000000 }); sampledGc = [bool]$SampledGc
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$runtime = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'LD_LIBRARY_PATH=/native',
    '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$workload,target=/work,readonly",
    '--mount', "type=bind,source=$run,target=/output", $runtimeImage, 'timeout', '-k', '2s', '20s', 'dotnet', '/work/CozNet.LinuxPerf.dll')
$outcomes = @()
& docker @runtime --verify-timer-layout *> (Join-Path $run 'layout.log')
$outcomes += @{ mode = 'layout'; exitCode = $LASTEXITCODE; recordExists = $true }
if ($ManagedWorkload -or $TransitionMatrix -or $SampledTimer) {
    $managedRuntime = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'LD_LIBRARY_PATH=/native:/native/signal',
        '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0', '--env', 'CORECLR_ENABLE_PROFILING=0',
        '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1',
        '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$workload,target=/work,readonly",
        '--mount', "type=bind,source=$run,target=/output", $runtimeImage, 'timeout', '-k', '2s', '20s', 'dotnet', '/work/CozNet.LinuxPerf.dll')
    if ($SampledTimer) {
        $imageIndex = [Array]::IndexOf($managedRuntime, $runtimeImage)
        $managedRuntime = @($managedRuntime[0..($imageIndex - 1)]) + @('--security-opt', 'seccomp=unconfined') + @($managedRuntime[$imageIndex..($managedRuntime.Count - 1)])
        $sampledCases = if ($SchedulerCost) {
            $random = [Random]::new(20261014)
            for ($block = 1; $block -le 8; $block++) {
                $modes = if ($random.Next(2) -eq 0) { @($false, $true) } else { @($true, $false) }
                foreach ($capture in $modes) {
                    $label = if ($capture) { 'enabled' } else { 'disabled' }
                    @{ name = "block-$block-$label"; percent = '0'; capture = $capture }
                }
            }
        } else {
            foreach ($percent in @('0', '50')) { @{ name = "sampled-$percent"; percent = $percent; capture = [bool]$SchedulerCapture } }
        }
        foreach ($case in $sampledCases) {
            $name = $case.name
            $sampledMode = if ($case.capture) { '--sampled-timer-scheduler' } elseif ($InitializeNativeTransport -and -not $ControllerPhases) { '--sampled-timer-capacity' } elseif ($InitializeNativeTransport) { '--sampled-timer-prepared' } elseif ($InitializeManagedPaths) { '--sampled-timer-initialized' } elseif ($ControllerPhases) { '--sampled-timer-phases' } else { '--sampled-timer' }
            $intervalArguments = @(if ($ShortInitialInterval) { '10us' })
            $workloadArguments = @($sampledMode, "/output/$name.json", $case.percent) + $intervalArguments
            if ($SampledGc) { $workloadArguments += 'gc' }
            & docker @managedRuntime @workloadArguments *> (Join-Path $run "$name.log")
            $outcomes += @{ mode = $name; arguments = $workloadArguments; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$name.json") }
            $outcomes | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'outcomes.json')
        }
    } elseif ($TransitionMatrix) {
        foreach ($scenario in @('jit', 'native-wait', 'managed-wait', 'transitions')) {
            foreach ($gcMode in @('no-gc', 'gc')) {
                $case = "$scenario-$gcMode"
                $intervalArguments = @(if ($ShortInitialInterval) { '10us' })
                $workloadArguments = @('--verify-timer-transition', "/output/$case.json", $scenario, $gcMode) + $intervalArguments
                & docker @managedRuntime @workloadArguments *> (Join-Path $run "$case.log")
                $outcomes += @{ mode = $case; arguments = $workloadArguments; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$case.json") }
            }
        }
    } else {
        $gcMode = if ($ForceGc) { 'gc' } else { 'no-gc' }
        & docker @managedRuntime --verify-timer-managed /output/managed.json $gcMode *> (Join-Path $run 'managed.log')
        $outcomes += @{ mode = 'managed'; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run 'managed.json') }
    }
} else {
    foreach ($mode in @('receipt', 'late', 'clock-failure')) {
        & docker @runtime --verify-timer-interop "/output/$mode.json" $mode *> (Join-Path $run "$mode.log")
        $outcomes += @{ mode = $mode; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$mode.json") }
    }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name] }
foreach ($path in $binaryPaths) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $binaries[[IO.Path]::GetFileName($path)] }
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; outcomes = $outcomes;
    generalClrSafetyEstablished = $false; sampledCapacityEstablished = $false; predictionAccuracyEstablished = $false
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Timer interop checks failed. Retain this run.' }
