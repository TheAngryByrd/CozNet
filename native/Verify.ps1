param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/native/evidence'))

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$profiler = Join-Path $root 'artifacts/native/CozNet.Profiler.dll'
$workload = Join-Path $root 'tests/CozNet.Instrumentation/bin/Release/net10.0/CozNet.Instrumentation.dll'
foreach ($required in @($profiler, $workload)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build output: $required" }
}
New-Item -ItemType Directory -Force $evidence | Out-Null
foreach ($mode in @('baseline', 'profiled', 'sampled', 'intervention')) {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false
    $start.ArgumentList.Add($workload)
    $start.ArgumentList.Add($mode)
    $start.ArgumentList.Add((Join-Path $evidence "$mode.json"))
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -match '^(CORECLR|COR)_.*PROFIL' -or $key -match '^(DOTNET|COMPlus)_(EnableDiagnostics|TieredCompilation|ReadyToRun)') {
            [void]$start.Environment.Remove($key)
        }
    }
    $start.Environment['DOTNET_TieredCompilation'] = '0'
    $start.Environment['DOTNET_ReadyToRun'] = '0'
    if ($mode -ne 'baseline') {
        $start.Environment['CORECLR_ENABLE_PROFILING'] = '1'
        $start.Environment['CORECLR_PROFILER'] = '{6E51C528-23F1-49EA-9346-2A7A8D911B61}'
        $start.Environment['CORECLR_PROFILER_PATH'] = $profiler
        $start.Environment['PATH'] = "$(Split-Path $profiler);$($start.Environment['PATH'])"
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$mode exceeded the 30-second process limit."
        }
        if ($process.ExitCode -ne 0) { throw "$mode failed with exit code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}
