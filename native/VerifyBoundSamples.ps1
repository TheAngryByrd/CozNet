param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/bound-samples'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$profiler = Join-Path $root 'artifacts/native-linux/integrated'
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
$image = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$sources = @('build/build.fs', 'native/VerifyBoundSamples.ps1', 'native/Profiler.cpp', 'native/ProbeLifetime.h', 'native/Sampler.h',
    'native/BuildLinux.ps1', 'native/GenerateCallbacks.ps1', 'tests/Shared/IL.cs', 'tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj')
$sources += @(Get-ChildItem (Join-Path $root 'tests/CozNet.LinuxPerf') -Filter '*.cs' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$hashes = @{}
foreach ($name in $sources) { $hashes[$name] = (Get-FileHash (Join-Path $root $name)).Hash }
$binaries = @{}
foreach ($file in @((Join-Path $profiler 'libCozNet.Profiler.so')) + @(Get-ChildItem $workload -Filter '*.dll' | ForEach-Object FullName)) {
    $binaries[$file] = (Get-FileHash $file).Hash
}
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '--mount', "type=bind,source=$workload,target=/workload,readonly",
    '--mount', "type=bind,source=$profiler,target=/native,readonly", '--mount', "type=bind,source=$run,target=/evidence")
& docker @container $image uname -r *> (Join-Path $run 'kernel.log')
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux kernel.' }
& docker @container $image timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll --verify-bound-samples-managed *> (Join-Path $run 'managed.log')
$managedExit = $LASTEXITCODE
$activation = @('--security-opt', 'seccomp=unconfined', '--env', 'DOTNET_PerfMapEnabled=0', '--env', 'DOTNET_TieredCompilation=0',
    '--env', 'DOTNET_ReadyToRun=0', '--env', 'CORECLR_ENABLE_PROFILING=1',
    '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}', '--env', 'CORECLR_PROFILER_PATH=/native/libCozNet.Profiler.so',
    '--env', 'LD_LIBRARY_PATH=/native', '--env', 'COZNET_PROBE_TARGET=linux-perf', '--env', 'COZNET_PROBE_IDENTITY=bound',
    '--env', 'COZNET_IL_MAP_MODE=registered', '--env', 'COZNET_PROBE_INTERVAL=1024')
$outcomes = @()
foreach ($mode in @('collect', 'before-enable', 'during', 'after-close')) {
    & docker @container @activation $image timeout -k 2s 30s dotnet /workload/CozNet.LinuxPerf.dll --verify-bound-samples "/evidence/$mode.json" $mode *> (Join-Path $run "$mode.log")
    $caseExit = $LASTEXITCODE
    $record = Join-Path $run "$mode.json"
    $passed = $false
    $recordError = $null
    try {
        $result = Get-Content $record -Raw | ConvertFrom-Json
        $passed = $result.passed -is [bool] -and $result.passed
    } catch { $recordError = $_.Exception.Message }
    $outcomes += @{ mode = $mode; exitCode = $caseExit; passed = $passed; recordError = $recordError }
}
$unchanged = $true
foreach ($name in $sources) { $unchanged = $unchanged -and (Get-FileHash (Join-Path $root $name)).Hash -eq $hashes[$name] }
foreach ($file in $binaries.Keys) { $unchanged = $unchanged -and (Get-FileHash $file).Hash -eq $binaries[$file] }
@{ sources = $hashes; binaries = $binaries; image = $image; source = 'Linux task-clock'; interval = 1024; runtimePerfMap = $false } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'manifest.json')
$passed = $unchanged -and $managedExit -eq 0 -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.passed }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; managedExit = $managedExit; outcomes = $outcomes;
    sampledPaymentCapacityEstablished = $false; predictionAccuracyEstablished = $false } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Bound CPU sample verification failed. Retain this run.' }
