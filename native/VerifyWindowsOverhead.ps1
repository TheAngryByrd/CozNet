param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/windows-overhead'))

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This launch adapter requires Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll'
$native = Join-Path $root 'artifacts/native'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
foreach ($stage in @('baseline', 'loaded', 'probes')) {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -match '^(CORECLR|COR)_.*PROFIL' -or $key -match '^(DOTNET|COMPlus)_(EnableDiagnostics|TieredCompilation|ReadyToRun|PerfMap|JitDisasm|JitStdOutFile)' -or $key.StartsWith('COZNET_')) {
            [void]$start.Environment.Remove($key)
        }
    }
    $start.Environment['DOTNET_TieredCompilation'] = '0'
    $start.Environment['DOTNET_ReadyToRun'] = '0'
    if ($stage -ne 'baseline') {
        $start.Environment['CORECLR_ENABLE_PROFILING'] = '1'
        $start.Environment['CORECLR_PROFILER'] = '{6E51C528-23F1-49EA-9346-2A7A8D911B61}'
        $start.Environment['CORECLR_PROFILER_PATH'] = Join-Path $native 'CozNet.Profiler.dll'
        $start.Environment['PATH'] = "$native;$($start.Environment['PATH'])"
        $start.Environment['COZNET_PROBE_TARGET'] = if ($stage -eq 'loaded') { 'instrumentation' } else { 'linux-perf' }
        $start.Environment['COZNET_PROBE_INTERVAL'] = '1024'
        $start.Environment['COZNET_IL_MAP_MODE'] = 'registered'
        $start.Environment['COZNET_PROBE_PLACEMENT'] = 'branch'
    }
    foreach ($argument in @($workload, '--overhead', (Join-Path $run "$stage.json"), $stage, '500')) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Windows overhead stage $stage exceeded the 30-second limit."
        }
        if ($process.ExitCode -ne 0) { throw "Windows overhead stage $stage failed with exit code $($process.ExitCode)." }
    } finally { $process.Dispose() }
}
@{ platform = 'windows-x64'; stages = @('baseline', 'loaded', 'probes'); measurementMilliseconds = 500;
    profilerSha256 = (Get-FileHash (Join-Path $native 'CozNet.Profiler.dll') -Algorithm SHA256).Hash;
    workloadSha256 = (Get-FileHash $workload -Algorithm SHA256).Hash; performanceComparisonEstablished = $false } |
    ConvertTo-Json | Set-Content (Join-Path $run 'environment.json')
$run
