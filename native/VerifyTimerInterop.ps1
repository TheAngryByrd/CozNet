param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/timer-interop'))
$ErrorActionPreference = 'Stop'
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
    'tests/CozNet.LinuxPerf/TargetLeaseReceipts.cs', 'tests/CozNet.LinuxPerf/TargetLeaseReceiptsChecks.cs',
    'tests/CozNet.LinuxPerf/ServiceReceiptTiming.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj', 'native/VerifyTimerInterop.ps1', 'build/build.fs')
$hashes = @{}
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash }
& docker @compiler uname -a *> (Join-Path $run 'kernel.txt')
if ($LASTEXITCODE -ne 0) { throw 'Kernel identity capture failed.' }
& docker @compiler cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Timer interop configuration failed. See $run/configure.log." }
& docker @compiler cmake --build /build --target CozNet.Timer *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Timer interop compilation failed. See $run/build.log." }
$binaryPaths = @((Join-Path $native 'libCozNet.Timer.so'), (Join-Path $workload 'CozNet.LinuxPerf.dll'), (Join-Path $workload 'CozNet.dll'))
$binaries = @{}
foreach ($path in $binaryPaths) { $binaries[[IO.Path]::GetFileName($path)] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
@{ sources = $hashes; binaries = $binaries; compilerImage = $compilerImage; runtimeImage = $runtimeImage;
    receiptBoundNanoseconds = 100000000; delayedReadMilliseconds = 150; watchdogSeconds = 20
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$runtime = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'LD_LIBRARY_PATH=/native',
    '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$workload,target=/work,readonly",
    '--mount', "type=bind,source=$run,target=/output", $runtimeImage, 'timeout', '-k', '2s', '20s', 'dotnet', '/work/CozNet.LinuxPerf.dll')
$outcomes = @()
& docker @runtime --verify-timer-layout *> (Join-Path $run 'layout.log')
$outcomes += @{ mode = 'layout'; exitCode = $LASTEXITCODE; recordExists = $true }
foreach ($mode in @('receipt', 'late', 'clock-failure')) {
    & docker @runtime --verify-timer-interop "/output/$mode.json" $mode *> (Join-Path $run "$mode.log")
    $outcomes += @{ mode = $mode; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$mode.json") }
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
