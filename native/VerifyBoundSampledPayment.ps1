param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/bound-sampled-payment'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$profiler = Join-Path $root 'artifacts/native-linux/integrated'
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$native = Join-Path $run 'native'
New-Item -ItemType Directory -Force $native | Out-Null
$sources = @('build/build.fs', 'native/VerifyBoundSampledPayment.ps1', 'native/AnalyzeBoundSampledPayment.py', 'native/BoundSampledPaymentAnalysisChecks.py', 'native/VerifyServiceOwner.ps1', 'native/Profiler.cpp', 'native/ProbeLifetime.h', 'native/Sampler.h', 'native/BuildLinux.ps1', 'native/GenerateCallbacks.ps1', 'tests/Shared/IL.cs', 'tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj', 'native/service/CMakeLists.txt')
$sources += @(Get-ChildItem (Join-Path $root 'tests/CozNet.LinuxPerf') -Filter '*.cs' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$sources += @(Get-ChildItem (Join-Path $root 'native/service') -File | Where-Object Extension -in '.cpp', '.h' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$sources += @(Get-ChildItem (Join-Path $root 'src/CozNet') -File | Where-Object Extension -in '.fs', '.fsi', '.fsproj' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$hashes = @{}
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash (Join-Path $root $name)).Hash }
& python (Join-Path $PSScriptRoot 'BoundSampledPaymentAnalysisChecks.py') *> (Join-Path $run 'analysis-checks.log')
$analysisChecksExit = $LASTEXITCODE
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
foreach ($check in @('--verify-bound-samples-managed', '--verify-bound-cooperative-managed')) {
    & docker @container $image timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll $check *> (Join-Path $run "$check.log")
    $managed += @{ check = $check; exitCode = $LASTEXITCODE }
}
$activation = @('--security-opt', 'seccomp=unconfined', '--env', 'DOTNET_PerfMapEnabled=0', '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
    '--env', 'CORECLR_ENABLE_PROFILING=1', '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}', '--env', 'CORECLR_PROFILER_PATH=/profiler/libCozNet.Profiler.so',
    '--env', 'LD_LIBRARY_PATH=/profiler:/native', '--env', 'COZNET_PROBE_TARGET=linux-perf', '--env', 'COZNET_PROBE_IDENTITY=bound', '--env', 'COZNET_IL_MAP_MODE=registered', '--env', 'COZNET_PROBE_INTERVAL=1024')
$outcomes = @()
foreach ($case in @('0', '50', 'invalidate-after-payment')) {
    $arguments = if ($case -eq 'invalidate-after-payment') { @('--invalidate-sampled-bound-cooperative', "/evidence/$case.json", '/native/libCozNet.Cooperative.so') }
        else { @('--sampled-bound-cooperative', "/evidence/$case.json", $case, '/native/libCozNet.Cooperative.so') }
    & docker @container @activation $image timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll @arguments *> (Join-Path $run "$case.log")
    $caseExit = $LASTEXITCODE
    $passed = $false
    $recordError = $null
    try { $result = Get-Content (Join-Path $run "$case.json") -Raw | ConvertFrom-Json; $passed = $result.passed -is [bool] -and $result.passed }
    catch { $recordError = $_.Exception.Message }
    $outcomes += @{ case = $case; exitCode = $caseExit; passed = $passed; recordError = $recordError }
}
& python (Join-Path $PSScriptRoot 'AnalyzeBoundSampledPayment.py') $run *> (Join-Path $run 'analysis.log')
$analysisExit = $LASTEXITCODE
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash (Join-Path $root $name)).Hash -eq $hashes[$name] }
foreach ($file in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash $file).Hash -eq $binaries[$file] }
@{ sources = $hashes; binaries = $binaries; image = $image; source = 'selected Linux task-clock'; interval = 1024; runtimePerfMap = $false; order = @('0', '50', 'invalidate-after-payment'); measurementSeconds = 2; debtLimitNanoseconds = 2000000 } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$passed = $unchanged -and $analysisChecksExit -eq 0 -and $analysisExit -eq 0 -and @($managed | Where-Object { $_.exitCode -ne 0 }).Count -eq 0 -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.passed }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; analysisChecksExit = $analysisChecksExit; analysisExit = $analysisExit; managed = $managed; outcomes = $outcomes; selectedPaymentSupported = $false; predictionAccuracyEstablished = $false } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Bound sampled payment failed. Retain this run.' }
