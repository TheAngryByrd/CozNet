param(
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/native-linux/evidence-integrated'),
    [ValidateSet(1, 64, 256, 1024, 8192)][int]$ProbeInterval = 1
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$native = Join-Path $root 'artifacts/native-linux/integrated'
$workload = Join-Path $root 'tests/CozNet.Instrumentation/bin/Release/net10.0'
$image = 'mcr.microsoft.com/dotnet/sdk:10.0.100'
foreach ($required in @((Join-Path $native 'libCozNet.Profiler.so'), (Join-Path $workload 'CozNet.Instrumentation.dll'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build output: $required" }
}
New-Item -ItemType Directory -Force $evidence | Out-Null
foreach ($mode in @('baseline', 'profiled', 'intervention', 'sampling-unavailable')) {
    $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m',
        '--mount', "type=bind,source=$workload,target=/work,readonly",
        '--mount', "type=bind,source=$native,target=/native,readonly",
        '--mount', "type=bind,source=$evidence,target=/evidence",
        '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
        '--env', 'COZNET_PROBE_TARGET=instrumentation', '--env', "COZNET_PROBE_INTERVAL=$ProbeInterval")
    if ($mode -ne 'baseline') {
        $arguments += @('--env', 'CORECLR_ENABLE_PROFILING=1',
            '--env', 'CORECLR_PROFILER={6E51C528-23F1-49EA-9346-2A7A8D911B61}',
            '--env', 'CORECLR_PROFILER_PATH=/native/libCozNet.Profiler.so', '--env', 'LD_LIBRARY_PATH=/native')
    }
    & docker @arguments $image timeout 30s dotnet /work/CozNet.Instrumentation.dll $mode "/evidence/$mode.json"
    if ($LASTEXITCODE -ne 0) { throw "Linux native verification failed in $mode mode." }
}
$imageId = & docker image inspect $image --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the verification image.' }
$kernel = & docker run --rm --pull never --network none --cap-drop ALL --read-only $image uname -r
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the verification kernel.' }
@{ image = $image; imageId = $imageId; kernel = $kernel;
    profilerSha256 = (Get-FileHash (Join-Path $native 'libCozNet.Profiler.so') -Algorithm SHA256).Hash } |
    ConvertTo-Json | Set-Content (Join-Path $evidence 'environment.json')
