param([string]$EvidenceDirectory, [switch]$Interleaved)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $root $(if ($Interleaved) { 'artifacts/interleaved-calibration' } else { 'artifacts/physical-calibration' })
}
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$snapshot = Join-Path $run 'workload'
New-Item -ItemType Directory -Force $snapshot | Out-Null
$workload = Join-Path $root 'tests/CozNet.LinuxPerf/bin/Release/net10.0'
$image = & docker image inspect mcr.microsoft.com/dotnet/sdk:10.0.100 --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'The Linux SDK image is unavailable.' }
$sourceNames = @(Get-ChildItem (Join-Path $root 'tests/CozNet.LinuxPerf') -File -Filter '*.cs' | ForEach-Object { 'tests/CozNet.LinuxPerf/' + $_.Name })
$sourceNames += @('tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj', 'native/VerifyPhysicalCalibration.ps1', 'build/build.fs')
$sources = @{}
foreach ($name in $sourceNames) {
    $source = Join-Path $root $name
    $destination = Join-Path (Join-Path $run 'source') $name
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    $sources[$name] = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
}
$binaries = @{}
foreach ($file in Get-ChildItem -LiteralPath $workload -File) {
    $destination = Join-Path $snapshot $file.Name
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    $binaries[$file.Name] = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
}
@{ capturedBeforeExecution = [DateTime]::UtcNow.ToString('O'); sources = $sources; binaries = $binaries; runtimeImage = $image
    cases = @('host-layout', 'linux-layout', 'physical-baseline', 'physical-optimized', 'calibration')
    calibrationCommand = $(if ($Interleaved) { '--calibrate-physical-interleaved' } else { '--calibrate-physical' })
    tieredCompilation = '0'; readyToRun = '0'; startupProfiler = '0'; watchdogSeconds = 30
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'manifest.json')
$runtime = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--env', 'DOTNET_TieredCompilation=0', '--env', 'DOTNET_ReadyToRun=0',
    '--env', 'CORECLR_ENABLE_PROFILING=0', '--mount', "type=bind,source=$snapshot,target=/work,readonly",
    '--mount', "type=bind,source=$run,target=/output", $image, 'timeout', '-k', '2s', '30s', 'dotnet', '/work/CozNet.LinuxPerf.dll')
$outcomes = @()
& dotnet (Join-Path $snapshot 'CozNet.LinuxPerf.dll') --verify-timer-layout *> (Join-Path $run 'host-layout.log')
$outcomes += @{ mode = 'host-layout'; exitCode = $LASTEXITCODE; recordExists = $true }
& docker @runtime --verify-timer-layout *> (Join-Path $run 'linux-layout.log')
$outcomes += @{ mode = 'linux-layout'; exitCode = $LASTEXITCODE; recordExists = $true }
foreach ($mode in @('baseline', 'optimized')) {
    & docker @runtime --timer-physical "/output/physical-$mode.json" $mode *> (Join-Path $run "physical-$mode.log")
    $outcomes += @{ mode = "physical-$mode"; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run "physical-$mode.json") }
}
$calibrationCommand = if ($Interleaved) { '--calibrate-physical-interleaved' } else { '--calibrate-physical' }
& docker @runtime $calibrationCommand /output/calibration.json *> (Join-Path $run 'calibration.log')
$outcomes += @{ mode = 'calibration'; exitCode = $LASTEXITCODE; recordExists = Test-Path -LiteralPath (Join-Path $run 'calibration.json') }
$outcomes | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'outcomes.json')
$unchanged = $true
foreach ($name in $sourceNames) {
    $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash -eq $sources[$name]
}
foreach ($name in $binaries.Keys) {
    $unchanged = $unchanged -and (Get-FileHash -LiteralPath (Join-Path $snapshot $name) -Algorithm SHA256).Hash -eq $binaries[$name]
}
$passed = $unchanged -and @($outcomes | Where-Object { $_.exitCode -ne 0 -or -not $_.recordExists }).Count -eq 0
@{ passed = $passed; inputsUnchanged = $unchanged; outcomes = $outcomes; predictionAccuracyEstablished = $false
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'Physical calibration checks failed. Retain this run.' }
