param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-signal-waits'))
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'VerifyLinuxSignal.ps1') -EvidenceDirectory (Join-Path $run 'basic') -CompareWaitModes *> (Join-Path $run 'basic.log')
$basicExit = $LASTEXITCODE
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'VerifyLinuxSampledSignals.ps1') -EvidenceDirectory (Join-Path $run 'sampled') -Cadence qualified-sample -Startup warm -Repetitions 3 -CompareWaitModes *> (Join-Path $run 'sampled.log')
$sampledExit = $LASTEXITCODE
@{ basicExitCode=$basicExit; sampledExitCode=$sampledExit; passed=($basicExit -eq 0 -and $sampledExit -eq 0) } | ConvertTo-Json | Set-Content (Join-Path $run 'summary.json')
Write-Host "Wait comparison evidence: $run"
if ($basicExit -ne 0 -or $sampledExit -ne 0) { throw 'Wait comparison failed. Retain every outcome.' }
