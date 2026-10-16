param(
    [ValidateSet('Host', 'LinuxContainer')][string]$Platform = 'Host',
    [switch]$Injected,
    [switch]$ProbeIdentity,
    [switch]$BoundCooperative,
    [switch]$OwnerController,
    [string]$ProfilerDirectory,
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/cooperative-interop')
)
$ErrorActionPreference = 'Stop'
if ($OwnerController -and -not $BoundCooperative) { throw 'Owner controller interop requires BoundCooperative.' }
if ($ProbeIdentity -and $BoundCooperative) { throw 'Select probe identity or bound cooperative checks.' }
if ($ProbeIdentity -or $BoundCooperative) { $Injected = $true }
. (Join-Path $PSScriptRoot 'ReadBoundSampledOutcome.ps1')
$contractMode = if ($BoundCooperative -or $ProbeIdentity) { '--verify-bound-cooperative-managed' } else { '--verify-cooperative-managed' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$native = Join-Path $run 'native'
New-Item -ItemType Directory -Force $native | Out-Null
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
if ($Injected) {
    if (-not $ProfilerDirectory) {
        $ProfilerDirectory = Join-Path $root $(if ($Platform -eq 'LinuxContainer') { 'artifacts/native-linux/integrated' } else { 'artifacts/native' })
    }
    $ProfilerDirectory = [IO.Path]::GetFullPath($ProfilerDirectory)
    $profilerName = if ($Platform -eq 'LinuxContainer' -or $IsLinux) { 'libCozNet.Profiler.so' } elseif ($IsMacOS) { 'libCozNet.Profiler.dylib' } else { 'CozNet.Profiler.dll' }
    $profiler = Join-Path $ProfilerDirectory $profilerName
    if (-not (Test-Path -LiteralPath $profiler)) { throw "Build the native profiler first: $profiler" }
}
$sources = @('native/service/CooperativeApi.cpp', 'native/service/CooperativeApi.h',
    'native/service/CooperativeSpinTiming.cpp', 'native/service/CooperativeSpinTiming.h',
    'native/service/CooperativeTarget.cpp', 'native/service/CooperativeTarget.h',
    'native/service/CooperativeProgress.h',
    'native/service/TargetLease.cpp', 'native/service/TargetLease.h', 'native/service/LeaseMailbox.h', 'native/service/TimerJournalSnapshot.h',
    'native/service/CMakeLists.txt', 'tests/CozNet.LinuxPerf/CooperativeReceipts.cs',
    'tests/CozNet.LinuxPerf/CooperativeAccountingAdapter.cs', 'tests/CozNet.LinuxPerf/NativeCooperativeClient.cs',
    'tests/CozNet.LinuxPerf/CooperativeProgressCapture.cs', 'tests/CozNet.LinuxPerf/CooperativeProgressCaptureChecks.cs',
    'tests/CozNet.LinuxPerf/CooperativeManagedChecks.cs', 'tests/CozNet.LinuxPerf/CooperativeInteropChecks.cs',
    'tests/CozNet.LinuxPerf/ServiceReceiptTiming.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'src/CozNet/SignalAccounting.fs', 'src/CozNet/SignalService.fs', 'src/CozNet/DelayLedger.fs', 'src/CozNet/SampleCredits.fs',
    'native/VerifyCooperativeInterop.ps1', 'native/VerifyServiceOwner.ps1', 'build/build.fs')
$sources += @('tests/CozNet.LinuxPerf/CooperativeFailureCapture.cs', 'tests/CozNet.LinuxPerf/CooperativeFailureCaptureChecks.cs')
$hashes = @{}
$sources += @('native/ReadBoundSampledOutcome.ps1', 'native/BoundSampledOutcomeChecks.ps1')
$sources += @('tests/CozNet.LinuxPerf/BoundControllerPlan.cs', 'tests/CozNet.LinuxPerf/BoundControllerPlanChecks.cs')
if ($ProbeIdentity -or $BoundCooperative) {
    $sources += @('tests/CozNet.LinuxPerf/ClrBoundInstructionMap.cs', 'tests/CozNet.LinuxPerf/ClrBoundInstructionMapChecks.cs',
        'tests/CozNet.LinuxPerf/ClrProbeLifetime.cs', 'tests/CozNet.LinuxPerf/ClrProbeLifetimeChecks.cs', 'tests/CozNet.LinuxPerf/InstructionMap.cs')
}
if ($BoundCooperative) { $sources += @('tests/CozNet.LinuxPerf/ClrProbeLifetime.cs', 'tests/CozNet.LinuxPerf/ClrProbeLifetimeChecks.cs', 'tests/CozNet.LinuxPerf/BoundCooperativeClient.cs', 'tests/CozNet.LinuxPerf/BoundCooperativeChecks.cs') }
if ($Injected) { $sources += @('native/Profiler.cpp', 'native/ProbeLifetime.h', 'native/service/ProbeLifetimeChecks.cpp', 'native/Sampler.h', 'native/GenerateCallbacks.ps1', 'tests/CozNet.LinuxPerf/InjectedProbes.cs', 'tests/CozNet.LinuxPerf/ProbeIdentityChecks.cs') }
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash }
& (Join-Path $PSScriptRoot 'VerifyServiceOwner.ps1') -Platform $Platform -OutputDirectory $native *> (Join-Path $run 'native.log')
$binary = if ($Platform -eq 'LinuxContainer' -or $IsLinux) { 'libCozNet.Cooperative.so' } elseif ($IsMacOS) { 'libCozNet.Cooperative.dylib' } else { 'Release/CozNet.Cooperative.dll' }
$library = Join-Path $native $binary
$binaries = @{}
if ($Injected) { $binaries[$profiler] = (Get-FileHash -LiteralPath $profiler -Algorithm SHA256).Hash }
foreach ($file in @($library, (Join-Path $workload 'CozNet.LinuxPerf.dll'), (Join-Path $workload 'CozNet.dll'))) {
    $binaries[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
function Invoke-HostCase([string[]]$Arguments, [string]$Name, [int]$Interval = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -match '^(CORECLR|COR)_.*PROFIL' -or $key -match '^COZNET_' -or $key -match '^(DOTNET|COMPlus)_(EnableDiagnostics|TieredCompilation|ReadyToRun)') {
            [void]$start.Environment.Remove($key)
        }
    }
    if ($Interval -ne 0) {
        $start.Environment['DOTNET_TieredCompilation'] = '0'
        $start.Environment['DOTNET_ReadyToRun'] = '0'
        $start.Environment['CORECLR_ENABLE_PROFILING'] = '1'
        $start.Environment['CORECLR_PROFILER'] = '{6E51C528-23F1-49EA-9346-2A7A8D911B61}'
        $start.Environment['CORECLR_PROFILER_PATH'] = $profiler
        $start.Environment['COZNET_PROBE_TARGET'] = 'linux-perf'
        $start.Environment['COZNET_PROBE_INTERVAL'] = "$Interval"
        $start.Environment['COZNET_IL_MAP_MODE'] = 'registered'
        $start.Environment['COZNET_PROBE_PLACEMENT'] = 'branch'
        $start.Environment['COZNET_PROBE_IDENTITY'] = $(if ($ProbeIdentity -or $BoundCooperative) { 'bound' } else { 'legacy' })
        $start.Environment['PATH'] = "$ProfilerDirectory$([IO.Path]::PathSeparator)$($start.Environment['PATH'])"
        if ($IsLinux) { $start.Environment['LD_LIBRARY_PATH'] = $ProfilerDirectory }
        if ($IsMacOS) { $start.Environment['DYLD_LIBRARY_PATH'] = $ProfilerDirectory }
    }
    $start.Environment['COZNET_OWNER_CONTROLLER'] = [string][int][bool]$OwnerController
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $exited = $process.WaitForExit(30000)
        if (-not $exited) { $process.Kill($true); $process.WaitForExit() }
        [IO.File]::WriteAllText((Join-Path $run "$Name.stdout.log"), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $run "$Name.stderr.log"), $stderr.GetAwaiter().GetResult())
        if ($exited) { return $process.ExitCode }
        return 124
    } finally { $process.Dispose() }
}
$runtimeImage = $null
if ($Platform -eq 'LinuxContainer') {
    $runtimeImage = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
    if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
    $container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$workload,target=/workload,readonly",
        '--mount', "type=bind,source=$run,target=/evidence")
    & docker @container $runtimeImage timeout -k 2s 25s dotnet /workload/CozNet.LinuxPerf.dll $contractMode *> (Join-Path $run 'managed-checks.log')
    $contractExit = $LASTEXITCODE
} else {
    $contractExit = Invoke-HostCase @((Join-Path $workload 'CozNet.LinuxPerf.dll'), $contractMode) 'managed-checks'
}
$outcomes = @()
$cases = if ($BoundCooperative) {
    @(foreach ($interval in @(1, 64, 256, 1024)) {
        foreach ($mode in @('paid', 'before-submit', 'before-poll', 'before-receipt', 'after-settlement')) {
            @{ mode = $mode; interval = $interval; name = "bound-$mode-$interval" }
        }
    })
}
elseif ($ProbeIdentity) {
    @(foreach ($interval in @(1, 64, 256, 1024)) {
        foreach ($mode in @('identity', 'unload', 'duplicate-module')) { @{ mode = $mode; interval = $interval; name = "$mode-$interval" } }
    })
}
elseif ($Injected) { @(1, 64, 256, 1024 | ForEach-Object { @{ mode = 'injected-paid'; interval = $_; name = "injected-$_" } }) }
else { @('paid', 'deferred', 'cancelled', 'late', 'clock-failure', 'active-cleanup', 'diagnostic', 'cpu', 'progress' | ForEach-Object { @{ mode = $_; interval = 0; name = $_ } }) }
foreach ($case in $cases) {
    $mode = $case.mode
    $name = $case.name
    $record = Join-Path $run "$name.json"
    if ($Platform -eq 'LinuxContainer') {
        $activation = @()
        if ($Injected) {
            $activation = @('--mount', "type=bind,source=$ProfilerDirectory,target=/profiler,readonly",
                '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
                '--env', 'CORECLR_ENABLE_PROFILING=1', '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}',
                '--env', "CORECLR_PROFILER_PATH=/profiler/$profilerName", '--env', 'LD_LIBRARY_PATH=/profiler',
                '--env', 'COZNET_PROBE_TARGET=linux-perf', '--env', "COZNET_PROBE_INTERVAL=$($case.interval)",
                '--env', 'COZNET_IL_MAP_MODE=registered', '--env', 'COZNET_PROBE_PLACEMENT=branch')
            $activation += @('--env', "COZNET_PROBE_IDENTITY=$(if ($ProbeIdentity -or $BoundCooperative) { 'bound' } else { 'legacy' })")
        }
        $activation += @('--env', ('COZNET_OWNER_CONTROLLER=' + [int][bool]$OwnerController))
        $caseArguments = if ($BoundCooperative) { @('--verify-bound-cooperative', "/evidence/$name.json", $mode, "/evidence/native/$binary") }
        elseif ($ProbeIdentity) { @('--verify-probe-identity', "/evidence/$name.json", $mode) }
        else { @('--verify-cooperative-interop', "/evidence/$name.json", $mode, "/evidence/native/$binary") }
        & docker @container @activation $runtimeImage timeout -k 2s 25s dotnet /workload/CozNet.LinuxPerf.dll @caseArguments *> (Join-Path $run "$name.log")
        $exitCode = $LASTEXITCODE
    } else {
        $caseArguments = if ($BoundCooperative) { @('--verify-bound-cooperative', $record, $mode, $library) }
        elseif ($ProbeIdentity) { @('--verify-probe-identity', $record, $mode) }
        else { @('--verify-cooperative-interop', $record, $mode, $library) }
        $exitCode = Invoke-HostCase (@((Join-Path $workload 'CozNet.LinuxPerf.dll')) + $caseArguments) $name $case.interval
    }
    $expectedOwner = if ($BoundCooperative) { [bool]$OwnerController } else { $null }
    $outcome = Read-BoundSampledOutcome -Path $record -Case $mode -ExitCode $exitCode -ExpectedOwnerController $expectedOwner
    $outcomes += @{ mode = $mode; interval = $case.interval; exitCode = $exitCode;
        recordExists = (Test-Path -LiteralPath $record); passed = $outcome.passed; recordError = $outcome.recordError }

}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name] }
foreach ($file in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $binaries[$file] }
@{ sources = $hashes; binaries = $binaries; platform = $Platform; ownerControllerEnabled = [bool]$OwnerController; runtimeImage = $runtimeImage } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$passed = $unchanged -and $contractExit -eq 0 -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.passed -or $_.recordError }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; contractExitCode = $contractExit; ownerControllerEnabled = [bool]$OwnerController; outcomes = $outcomes;
    sampledCapacityEstablished = $false; generalClrSafetyEstablished = $false; predictionAccuracyEstablished = $false
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Cooperative managed interop checks failed. Retain this run.' }
