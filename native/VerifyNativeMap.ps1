param(
    [Parameter(Mandatory)][ValidateSet('windows', 'linux')][string]$Platform,
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/native-map')
)

$ErrorActionPreference = 'Stop'
if ($Platform -eq 'windows' -and -not $IsWindows) { throw 'The Windows mapping diagnostic requires a Windows host.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ($Platform + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence "$Platform-last-run.txt"), $run)
$workload = Join-Path $root 'tests/CozNet.NativeMap/bin/Release/net10.0'
$native = Join-Path $root $(if ($Platform -eq 'windows') { 'artifacts/native' } else { 'artifacts/native-linux/integrated' })
$library = if ($Platform -eq 'windows') { 'CozNet.Profiler.dll' } else { 'libCozNet.Profiler.so' }
$image = 'mcr.microsoft.com/dotnet/sdk:10.0.100'
foreach ($required in @((Join-Path $native $library), (Join-Path $workload 'CozNet.NativeMap.dll'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build output: $required" }
}
foreach ($target in @('instrumentation', 'linux-perf')) {
    foreach ($mode in @('registered', 'raw-diagnostic')) {
      foreach ($placement in @('branch', 'empty-stack-diagnostic')) {
        $name = "$target-$mode-$placement"
        $method = if ($target -eq 'instrumentation') { 'CozNet.Instrumentation.Workload:Work' } else { 'CpuWorkload:Run' }
        $environment = @{
            DOTNET_TieredCompilation = '0'; DOTNET_ReadyToRun = '0'; CORECLR_ENABLE_PROFILING = '1';
            CORECLR_PROFILER = '{6E51C528-23F1-49EA-9346-2A7A8D911B61}'; COZNET_PROBE_TARGET = $target;
            COZNET_PROBE_INTERVAL = '1024'; COZNET_IL_MAP_MODE = $mode; COZNET_PROBE_PLACEMENT = $placement; DOTNET_JitDisasm = $method
        }
        if ($Platform -eq 'windows') {
            $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
            $start.UseShellExecute = $false
            foreach ($key in @($start.Environment.Keys)) {
                if ($key -match '^(CORECLR|COR)_.*PROFIL' -or $key -match '^(DOTNET|COMPlus)_(EnableDiagnostics|TieredCompilation|ReadyToRun|JitDisasm|JitStdOutFile)') {
                    [void]$start.Environment.Remove($key)
                }
            }
            foreach ($key in $environment.Keys) { $start.Environment[$key] = $environment[$key] }
            $start.Environment['CORECLR_PROFILER_PATH'] = Join-Path $native $library
            $start.Environment['PATH'] = "$native;$($start.Environment['PATH'])"
            $start.Environment['DOTNET_JitStdOutFile'] = Join-Path $run "$name.asm"
            foreach ($argument in @((Join-Path $workload 'CozNet.NativeMap.dll'), $target, $mode, $placement, (Join-Path $run "$name.json"))) {
                $start.ArgumentList.Add($argument)
            }
            $process = [Diagnostics.Process]::Start($start)
            try {
                if (-not $process.WaitForExit(30000)) {
                    $process.Kill($true)
                    $process.WaitForExit()
                    throw "$name exceeded the 30-second process limit."
                }
                if ($process.ExitCode -ne 0) { throw "$name failed with exit code $($process.ExitCode)." }
            } finally { $process.Dispose() }
        } else {
            $arguments = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
                '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m', '--mount', "type=bind,source=$workload,target=/work,readonly",
                '--mount', "type=bind,source=$native,target=/native,readonly", '--mount', "type=bind,source=$run,target=/evidence",
                '--env', "CORECLR_PROFILER_PATH=/native/$library", '--env', 'LD_LIBRARY_PATH=/native',
                '--env', "DOTNET_JitStdOutFile=/evidence/$name.asm")
            foreach ($key in $environment.Keys) { $arguments += @('--env', "$key=$($environment[$key])") }
            & docker @arguments $image timeout 30s dotnet /work/CozNet.NativeMap.dll $target $mode $placement "/evidence/$name.json"
            if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE." }
        }
        $disassembly = Join-Path $run "$name.asm"
        if (-not (Test-Path -LiteralPath $disassembly) -or (Get-Item -LiteralPath $disassembly).Length -eq 0) {
            throw "The JIT did not write disassembly for $name."
        }
    }
}
}
$identity = $null
if ($Platform -eq 'linux') {
    $identity = & docker image inspect $image --format '{{.Id}}'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Linux mapping image.' }
}
@{ platform = $Platform; image = $(if ($Platform -eq 'linux') { $image } else { $null }); imageId = $identity;
    profilerSha256 = (Get-FileHash (Join-Path $native $library) -Algorithm SHA256).Hash } |
    ConvertTo-Json | Set-Content (Join-Path $run 'environment.json')
