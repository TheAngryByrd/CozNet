param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/abort-fixtures/linux'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$tool = Join-Path $root 'lode/tmp/diagnostic-tools/.store/dotnet-trace/10.0.750501/dotnet-trace/10.0.750501/tools/net8.0/any'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
$image = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$files = @('tests/CozNet.LinuxPerf/SampledSignalTrace.cs', 'tests/CozNet.LinuxPerf/SampledSignalAbortChecks.cs',
    'tests/CozNet.LinuxPerf/SampledSignalWorkload.cs', 'tests/CozNet.LinuxPerf/Program.cs',
    'native/AnalyzeRuntimeSuspension.py', 'native/RuntimeSuspensionChecks.py', 'native/AbortTraceChecks.py',
    'native/VerifyLinuxAbortTraces.ps1', 'native/RuntimeTraceReader.cs', 'native/ExportRuntimeTrace.ps1',
    'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll', 'tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.dll')
$hashes = @{}
foreach ($file in $files) {
    $hashes[$file] = (Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash
    $destination = Join-Path $run "snapshot/$file"
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination
}
@{ imageId=$image; sourceHashes=$hashes; collectorSha256=(Get-FileHash (Join-Path $tool 'dotnet-trace.dll') -Algorithm SHA256).Hash;
    expectedWorkloadPassed=$false; expectedCorrelationValid=$true; watchdogSeconds=30; stages=@('cold','warm') } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'environment.json')
$outcomes = [Collections.Generic.List[object]]::new()
foreach ($stage in @('cold', 'warm')) {
    $directory = Join-Path $run $stage
    New-Item -ItemType Directory -Force $directory | Out-Null
    & docker run --rm --pull never --network none --cap-drop ALL --read-only --tmpfs '/tmp:rw,nosuid,size=128m' `
        --env DOTNET_TieredCompilation=0 --env DOTNET_ReadyToRun=0 --env CORECLR_ENABLE_PROFILING=0 `
        --mount "type=bind,source=$workload,target=/work,readonly" --mount "type=bind,source=$tool,target=/tool,readonly" `
        --mount "type=bind,source=$directory,target=/output" $image timeout -k 2s 30s dotnet /tool/dotnet-trace.dll collect `
        --providers 'Microsoft-Windows-DotNETRuntime:0x1:4,CozNet-SampledSignals:0x0:4' --buffersize 64 --rundown false `
        --output /output/trace.nettrace -- dotnet /work/CozNet.LinuxPerf.dll --verify-signal-trace-abort /output/workload.json $stage *> (Join-Path $directory 'collect.log')
    $exitCode = $LASTEXITCODE
    $analysisExit = -1
    if ((Test-Path (Join-Path $directory 'workload.json')) -and (Test-Path (Join-Path $directory 'trace.nettrace'))) {
        & (Join-Path $PSScriptRoot 'ExportRuntimeTrace.ps1') -TracePath (Join-Path $directory 'trace.nettrace') -OutputPath (Join-Path $directory 'events.json')
        & python (Join-Path $PSScriptRoot 'AnalyzeRuntimeSuspension.py') (Join-Path $directory 'workload.json') (Join-Path $directory 'events.json') (Join-Path $directory 'correlation.json')
        $analysisExit = $LASTEXITCODE
        $analysis = Get-Content (Join-Path $directory 'correlation.json') -Raw | ConvertFrom-Json
        if ($analysis.workloadAborted -ne $true -or $analysis.workloadPassed -ne $false -or $analysis.workloadProtocolPassed -ne $false) {
            $analysisExit = 1
        }
    }
    $outcomes.Add(@{ stage=$stage; fixtureExit=$exitCode; correlationExit=$analysisExit })
    $outcomes | ConvertTo-Json | Set-Content (Join-Path $run 'outcomes.json')
}
$unchanged = $true
foreach ($file in $files) {
    if ((Get-FileHash (Join-Path $root $file) -Algorithm SHA256).Hash -ne $hashes[$file]) { $unchanged = $false }
}
$passed = $unchanged -and @($outcomes | Where-Object { $_.fixtureExit -ne 0 -or $_.correlationExit -ne 0 }).Count -eq 0
@{ passed=$passed; sourcesAndBinariesUnchanged=$unchanged; outcomes=$outcomes.ToArray() } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'summary.json')
if (-not $passed) { throw "Abort trace verification failed. Retain all outcomes: $run" }
$run
