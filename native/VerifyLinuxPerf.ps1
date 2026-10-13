param(
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-perf'),
    [ValidateSet('managed', 'linux-nanosleep')][string]$WaitBackend = 'managed',
    [ValidateSet(0, 10)][int]$ProbeCpuMilliseconds = 10,
    [ValidateSet('manual', 'injected')][string]$ProbeMode = 'manual',
    [ValidateSet(1, 64, 256, 1024)][int]$ProbeInterval = 1
)

$ErrorActionPreference = 'Stop'
if ($ProbeMode -eq 'manual' -and $ProbeInterval -ne 1) { throw 'Manual probes require interval 1.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
if (-not (Test-Path -LiteralPath (Join-Path $workload 'CozNet.LinuxPerf.dll'))) {
    throw 'Build the Linux perf workload in Release first.'
}
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $evidence | Out-Null
$image = 'mcr.microsoft.com/dotnet/sdk:10.0.100'
$identity = & docker image inspect $image --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw "The local container image is unavailable: $image" }
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $image uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux container kernel.' }

foreach ($policy in @('default', 'unfiltered', 'control')) {
    $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL',
        '--read-only', '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m',
        '--env', 'DOTNET_PerfMapEnabled=3', '--env', 'DOTNET_PerfMapShowOptimizationTiers=1',
        '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
        '--mount', "type=bind,source=$workload,target=/work,readonly",
        '--mount', "type=bind,source=$evidence,target=/output")
    if ($policy -ne 'default') { $arguments += @('--security-opt', 'seccomp=unconfined') }
    $arguments += @('--env', "COZNET_PROBE_MODE=$ProbeMode", '--env', "COZNET_PROBE_INTERVAL=$ProbeInterval")
    if ($ProbeMode -eq 'injected') {
        $native = Join-Path $root 'artifacts/native-linux/integrated'
        $arguments += @('--mount', "type=bind,source=$native,target=/native,readonly",
            '--env', 'CORECLR_ENABLE_PROFILING=1',
            '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}',
            '--env', 'CORECLR_PROFILER_PATH=/native/libCozNet.Profiler.so',
            '--env', 'LD_LIBRARY_PATH=/native', '--env', 'COZNET_PROBE_TARGET=linux-perf')
    }
    $speedup = if ($policy -eq 'control') { '0' } else { '50' }
    $arguments += @($image, 'timeout', '30s', 'dotnet', '/work/CozNet.LinuxPerf.dll', "/output/$policy.json", $speedup, $WaitBackend, "$ProbeCpuMilliseconds")
    & docker @arguments
    $code = $LASTEXITCODE
    if ($policy -eq 'default' -and $code -eq 2) {
        $result = Get-Content -LiteralPath (Join-Path $evidence 'default.json') -Raw | ConvertFrom-Json
        if ($result.available -ne $false -or $result.errno -notin @(1, 13)) {
            throw 'The default-policy probe failed without an access-denied result.'
        }
    } elseif ($code -ne 0) {
        throw "Linux perf verification failed under $policy policy with exit code $code."
    }
}
@{ image = $image; imageId = $identity; kernel = $kernel; capabilities = 'none'; network = 'none'; probeMode = $ProbeMode; probeInterval = $ProbeInterval } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'environment.json')
