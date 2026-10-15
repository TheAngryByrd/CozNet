param(
    [ValidateSet('Host', 'LinuxContainer')][string]$Platform = 'Host',
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/cooperative-interop')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$native = Join-Path $run 'native'
New-Item -ItemType Directory -Force $native | Out-Null
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$sources = @('native/service/CooperativeApi.cpp', 'native/service/CooperativeApi.h',
    'native/service/CooperativeSpinTiming.cpp', 'native/service/CooperativeSpinTiming.h',
    'native/service/CooperativeTarget.cpp', 'native/service/CooperativeTarget.h',
    'native/service/TargetLease.cpp', 'native/service/TargetLease.h', 'native/service/LeaseMailbox.h', 'native/service/TimerJournalSnapshot.h',
    'native/service/CMakeLists.txt', 'tests/CozNet.LinuxPerf/CooperativeReceipts.cs',
    'tests/CozNet.LinuxPerf/CooperativeAccountingAdapter.cs', 'tests/CozNet.LinuxPerf/NativeCooperativeClient.cs',
    'tests/CozNet.LinuxPerf/CooperativeManagedChecks.cs', 'tests/CozNet.LinuxPerf/CooperativeInteropChecks.cs',
    'tests/CozNet.LinuxPerf/ServiceReceiptTiming.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'src/CozNet/SignalAccounting.fs', 'src/CozNet/SignalService.fs', 'src/CozNet/DelayLedger.fs', 'src/CozNet/SampleCredits.fs',
    'native/VerifyCooperativeInterop.ps1', 'native/VerifyServiceOwner.ps1', 'build/build.fs')
$hashes = @{}
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash }
& (Join-Path $PSScriptRoot 'VerifyServiceOwner.ps1') -Platform $Platform -OutputDirectory $native *> (Join-Path $run 'native.log')
$binary = if ($Platform -eq 'LinuxContainer' -or $IsLinux) { 'libCozNet.Cooperative.so' } elseif ($IsMacOS) { 'libCozNet.Cooperative.dylib' } else { 'Release/CozNet.Cooperative.dll' }
$library = Join-Path $native $binary
$binaries = @{}
foreach ($file in @($library, (Join-Path $workload 'CozNet.LinuxPerf.dll'), (Join-Path $workload 'CozNet.dll'))) {
    $binaries[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
function Invoke-HostCase([string[]]$Arguments, [string]$Name) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
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
        '--mount', "type=bind,source=$run,target=/evidence", $runtimeImage)
    & docker @container timeout -k 2s 25s dotnet /workload/CozNet.LinuxPerf.dll --verify-cooperative-managed *> (Join-Path $run 'managed-checks.log')
    $contractExit = $LASTEXITCODE
} else {
    $contractExit = Invoke-HostCase @((Join-Path $workload 'CozNet.LinuxPerf.dll'), '--verify-cooperative-managed') 'managed-checks'
}
$outcomes = @()
foreach ($mode in @('paid', 'deferred', 'cancelled', 'late', 'clock-failure', 'active-cleanup')) {
    $record = Join-Path $run "$mode.json"
    if ($Platform -eq 'LinuxContainer') {
        & docker @container timeout -k 2s 25s dotnet /workload/CozNet.LinuxPerf.dll --verify-cooperative-interop "/evidence/$mode.json" $mode "/evidence/native/$binary" *> (Join-Path $run "$mode.log")
        $exitCode = $LASTEXITCODE
    } else {
        $exitCode = Invoke-HostCase @((Join-Path $workload 'CozNet.LinuxPerf.dll'), '--verify-cooperative-interop', $record, $mode, $library) $mode
    }
    $exists = Test-Path -LiteralPath $record
    $passed = $false
    $recordError = $null
    if ($exists) {
        try {
            $result = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
            $passed = $result.passed -is [bool] -and $result.passed
        } catch { $recordError = $_.Exception.Message }
    }
    $outcomes += @{ mode = $mode; exitCode = $exitCode; recordExists = $exists; passed = $passed; recordError = $recordError }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name] }
foreach ($file in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $binaries[$file] }
@{ sources = $hashes; binaries = $binaries; platform = $Platform; runtimeImage = $runtimeImage } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$passed = $unchanged -and $contractExit -eq 0 -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.passed }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; contractExitCode = $contractExit; outcomes = $outcomes;
    sampledCapacityEstablished = $false; generalClrSafetyEstablished = $false; predictionAccuracyEstablished = $false
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Cooperative managed interop checks failed. Retain this run.' }
