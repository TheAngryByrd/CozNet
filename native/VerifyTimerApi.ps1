param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/timer-api'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect coznet-native-build:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The native compiler image is unavailable.' }
$sources = @('native/service/TimerApi.h', 'native/service/TimerApi.Linux.cpp', 'native/service/TimerApiChecks.Linux.cpp',
    'native/service/LeaseMailbox.h', 'native/service/TargetLease.h', 'native/service/TargetLease.cpp',
    'native/service/TimerDeliveryWorkload.LinuxX64.S', 'native/service/CMakeLists.txt', 'native/VerifyTimerApi.ps1', 'build/build.fs')
$hashes = @{}
$sources += @('native/service/TimerJournalSnapshot.h', 'native/service/TimerJournalSnapshotChecks.cpp')
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash }
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly",
    '--mount', "type=bind,source=$run,target=/build", $image)
& docker @container uname -a *> (Join-Path $run 'kernel.txt')
if ($LASTEXITCODE -ne 0) { throw 'Kernel identity capture failed.' }
& docker @container cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DCOZ_SERVICE_LINUX_SIGNAL=ON *> (Join-Path $run 'configure.log')
if ($LASTEXITCODE -ne 0) { throw "Timer ABI configuration failed. See $run/configure.log." }
& docker @container cmake --build /build --target TimerApiChecks TargetLeaseChecks LeaseMailboxChecks TimerJournalSnapshotChecks *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Timer ABI compilation failed. See $run/build.log." }
$binaries = @{}
foreach ($name in @('libCozNet.Timer.so', 'TimerApiChecks')) { $binaries[$name] = (Get-FileHash -LiteralPath (Join-Path $run $name) -Algorithm SHA256).Hash }
@{ sources = $hashes; binaries = $binaries; image = $image; requestBoundNanoseconds = 50000;
    handlerBoundNanoseconds = 5000000; receiptBoundNanoseconds = 100000000; targets = 2; commands = 4
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'manifest.json')
& docker @container ctest --test-dir /build -R '^(target-lease|lease-mailbox|timer-journal-snapshot)$' --output-on-failure *> (Join-Path $run 'contracts.log')
$contractExit = $LASTEXITCODE
$outcomes = @()
foreach ($mode in @('deferred', 'chain')) {
    & docker @container timeout -k 2s 20s /build/TimerApiChecks $mode "/build/$mode.json" *> (Join-Path $run "$mode.log")
    $outcomes += @{ mode = $mode; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "$mode.json") }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $hashes[$name] }
foreach ($name in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $run $name) -Algorithm SHA256).Hash -eq $binaries[$name] }
$passed = $unchanged -and $contractExit -eq 0 -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; contractExitCode = $contractExit; outcomes = $outcomes;
    managedReceiptEstablished = $false; generalClrSafetyEstablished = $false; predictionAccuracyEstablished = $false
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Timer ABI checks failed. Retain this run.' }
