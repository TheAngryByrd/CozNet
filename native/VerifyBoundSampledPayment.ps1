param([string]$EvidenceDirectory, [switch]$SelectedPayment, [switch]$PauseTiming, [switch]$ChildCpu, [switch]$OwnerDrain, [switch]$BatchService, [switch]$DispatchTiming, [switch]$DirectReceiptReady,
    [ValidateSet('', 'continuous', 'qualified-sample')][string]$SelectedCadence = '',
    [ValidateRange(1, 2147483647)][int]$SelectedBatches = 5000)
$ErrorActionPreference = 'Stop'
if ($SelectedCadence -and -not $SelectedPayment) { throw 'Selected cadence requires selected payment.' }
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $PSScriptRoot $(if ($SelectedPayment) { '../artifacts/bound-selected-payment' } else { '../artifacts/bound-sampled-payment' })
}
if ($PauseTiming -and -not $SelectedPayment) { throw 'Pause timing requires selected payment.' }
if ($DirectReceiptReady -and -not $SelectedPayment) { throw 'Direct receipt readiness requires selected payment.' }
if ($DispatchTiming -and -not $SelectedPayment) { throw 'Dispatch timing requires selected payment.' }
if ($ChildCpu -and -not $SelectedPayment) { throw 'Child CPU capture requires selected payment.' }
if ($OwnerDrain -and (-not $SelectedPayment -or -not $SelectedCadence)) { throw 'Owner draining requires selected payment and an explicit cadence.' }
$analysisScript = if ($SelectedPayment) { 'AnalyzeBoundSelectedPayment.py' } else { 'AnalyzeBoundSampledPayment.py' }
$caseNames = if ($SelectedPayment) { @('0', '50', 'invalidate-after-selected-payment') } else { @('0', '50', 'invalidate-after-payment') }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$profiler = Join-Path $root 'artifacts/native-linux/integrated'
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$native = Join-Path $run 'native'
New-Item -ItemType Directory -Force $native | Out-Null
$sources = @('build/build.fs', 'native/VerifyBoundSampledPayment.ps1', 'native/AnalyzeBoundSampledPayment.py', 'native/BoundSampledPaymentAnalysisChecks.py', 'native/VerifyServiceOwner.ps1', 'native/Profiler.cpp', 'native/ProbeLifetime.h', 'native/Sampler.h', 'native/BuildLinux.ps1', 'native/GenerateCallbacks.ps1', 'tests/Shared/IL.cs', 'tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj', 'native/service/CMakeLists.txt')
$sources += @('native/AnalyzeBoundSelectedPayment.py', 'native/BoundSelectedPaymentAnalysisChecks.py')
$sources += 'native/VerifyBoundSelectedCadence.ps1'
$sources += @('native/ReadBoundSampledOutcome.ps1', 'native/BoundSampledOutcomeChecks.ps1')
$sources += @(Get-ChildItem (Join-Path $root 'tests/CozNet.LinuxPerf') -Filter '*.cs' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$sources += @(Get-ChildItem (Join-Path $root 'native/service') -File | Where-Object Extension -in '.cpp', '.h' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$sources += @(Get-ChildItem (Join-Path $root 'src/CozNet') -File | Where-Object Extension -in '.fs', '.fsi', '.fsproj' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$hashes = @{}
foreach ($name in $sources) {
    $hashes[$name] = (Get-FileHash (Join-Path $root $name)).Hash
    $copy = Join-Path $run "source/$name"
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($copy)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $copy
}
. (Join-Path $run 'source/native/ReadBoundSampledOutcome.ps1')
& pwsh -NoProfile -File (Join-Path $run 'source/native/BoundSampledOutcomeChecks.ps1') *> (Join-Path $run 'outcome-checks.log')
$outcomeChecksExit = $LASTEXITCODE
$analysisChecksExit = 0
$analysisChecks = @('BoundSampledPaymentAnalysisChecks.py')
if ($SelectedPayment) { $analysisChecks += 'BoundSelectedPaymentAnalysisChecks.py' }
foreach ($check in $analysisChecks) {
    & python (Join-Path $run "source/native/$check") *> (Join-Path $run "$check.log")
    if ($LASTEXITCODE -ne 0) { $analysisChecksExit = $LASTEXITCODE }
}
& (Join-Path $PSScriptRoot 'VerifyServiceOwner.ps1') -Platform LinuxContainer -OutputDirectory $native *> (Join-Path $run 'native.log')
$image = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$binaries = @{}
foreach ($file in @((Join-Path $profiler 'libCozNet.Profiler.so'), (Join-Path $native 'libCozNet.Cooperative.so')) + @(Get-ChildItem $workload -Filter '*.dll' | ForEach-Object FullName)) { $binaries[$file] = (Get-FileHash $file).Hash }
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m',
    '--mount', "type=bind,source=$workload,target=/workload,readonly", '--mount', "type=bind,source=$profiler,target=/profiler,readonly",
    '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$run,target=/evidence")
& docker @container $image uname -r *> (Join-Path $run 'kernel.log')
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux kernel.' }
$managed = @()
$managedChecks = @('--verify-bound-samples-managed', '--verify-bound-cooperative-managed')
if ($SelectedPayment) { $managedChecks += '--verify-pausable-managed' }
foreach ($check in $managedChecks) {
    & docker @container $image timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll $check *> (Join-Path $run "$check.log")
    $managed += @{ check = $check; exitCode = $LASTEXITCODE }
}
$activation = @('--security-opt', 'seccomp=unconfined', '--env', 'DOTNET_PerfMapEnabled=0', '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
    '--env', 'CORECLR_ENABLE_PROFILING=1', '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}', '--env', 'CORECLR_PROFILER_PATH=/profiler/libCozNet.Profiler.so',
    '--env', 'LD_LIBRARY_PATH=/profiler:/native', '--env', 'COZNET_PROBE_TARGET=linux-perf', '--env', 'COZNET_PROBE_IDENTITY=bound', '--env', 'COZNET_IL_MAP_MODE=registered', '--env', 'COZNET_PROBE_INTERVAL=1024')
$activation += @('--env', ('COZNET_PAUSE_TIMING=' + [int][bool]$PauseTiming))
$activation += @('--env', ('COZNET_CHILD_CPU=' + [int][bool]$ChildCpu))
$activation += @('--env', ('COZNET_OWNER_DRAIN=' + [int][bool]$OwnerDrain))
$activation += @('--env', ('COZNET_BATCH_SERVICE=' + [int][bool]$BatchService))
$activation += @('--env', ('COZNET_DISPATCH_TIMING=' + [int][bool]$DispatchTiming))
$activation += @('--env', ('COZNET_DIRECT_RECEIPT_READY=' + [int][bool]$DirectReceiptReady))
$outcomes = @()
foreach ($case in $caseNames) {
    $arguments = if ($SelectedCadence -and $case -eq 'invalidate-after-selected-payment') { @('--invalidate-sampled-bound-selected-cadence', "/evidence/$case.json", '/native/libCozNet.Cooperative.so', $SelectedCadence, [string]$SelectedBatches) }
        elseif ($SelectedCadence) { @('--sampled-bound-selected-cadence', "/evidence/$case.json", $case, '/native/libCozNet.Cooperative.so', $SelectedCadence, [string]$SelectedBatches) }
        elseif ($case -eq 'invalidate-after-selected-payment') { @('--invalidate-sampled-bound-selected-cooperative', "/evidence/$case.json", '/native/libCozNet.Cooperative.so') }
        elseif ($case -eq 'invalidate-after-payment') { @('--invalidate-sampled-bound-cooperative', "/evidence/$case.json", '/native/libCozNet.Cooperative.so') }
        elseif ($SelectedPayment) { @('--sampled-bound-selected-cooperative', "/evidence/$case.json", $case, '/native/libCozNet.Cooperative.so') }
        else { @('--sampled-bound-cooperative', "/evidence/$case.json", $case, '/native/libCozNet.Cooperative.so') }
    & docker @container @activation $image timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll @arguments *> (Join-Path $run "$case.log")
    $caseExit = $LASTEXITCODE
    $outcomes += Read-BoundSampledOutcome -Path (Join-Path $run "$case.json") -Case $case -ExitCode $caseExit -SelectedCadence $SelectedCadence -SelectedBatches $SelectedBatches -ExpectedPauseTiming ([bool]$PauseTiming) -ExpectedChildCpu ([bool]$ChildCpu) -ExpectedOwnerDrain ([bool]$OwnerDrain) -ExpectedReceiptCaptureVersion 1 -ExpectedBatchService ([bool]$BatchService) -ExpectedDispatchTiming ([bool]$DispatchTiming) -ExpectedDirectReceiptReady ([bool]$DirectReceiptReady)
}
& python (Join-Path $run "source/native/$analysisScript") $run *> (Join-Path $run 'analysis.log')
$analysisExit = $LASTEXITCODE
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash (Join-Path $root $name)).Hash -eq $hashes[$name] }
foreach ($file in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash $file).Hash -eq $binaries[$file] }
@{ sources = $hashes; binaries = $binaries; image = $image; source = 'selected Linux task-clock'; interval = 1024; runtimePerfMap = $false; order = $caseNames; selectedPaymentEnabled = [bool]$SelectedPayment; pauseTimingEnabled = [bool]$PauseTiming; childCpuEnabled = [bool]$ChildCpu; ownerDrainEnabled = [bool]$OwnerDrain; batchServiceEnabled = [bool]$BatchService; dispatchTimingEnabled = [bool]$DispatchTiming; directReceiptReadyEnabled = [bool]$DirectReceiptReady; selectedCadence = $SelectedCadence; requestedBatches = $(if ($SelectedCadence) { $SelectedBatches } else { $null }); measurementSeconds = $(if ($SelectedCadence) { $null } else { 2 }); debtLimitNanoseconds = 2000000 } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$passed = $unchanged -and $outcomeChecksExit -eq 0 -and $analysisChecksExit -eq 0 -and $analysisExit -eq 0 -and @($managed | Where-Object { $_.exitCode -ne 0 }).Count -eq 0 -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.passed -or $_.recordError }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; outcomeChecksExit = $outcomeChecksExit; analysisChecksExit = $analysisChecksExit; analysisExit = $analysisExit; managed = $managed; outcomes = $outcomes; selectedPaymentEnabled = [bool]$SelectedPayment; pauseTimingEnabled = [bool]$PauseTiming; childCpuEnabled = [bool]$ChildCpu; ownerDrainEnabled = [bool]$OwnerDrain; batchServiceEnabled = [bool]$BatchService; dispatchTimingEnabled = [bool]$DispatchTiming; directReceiptReadyEnabled = [bool]$DirectReceiptReady; selectedPaymentSupported = $false; predictionAccuracyEstablished = $false } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Bound sampled payment failed. Retain this run.' }
