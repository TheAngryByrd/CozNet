param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-spin-reliability'))
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'VerifyLinuxSignalTransitions.ps1') -EvidenceDirectory (Join-Path $run 'transitions') -WaitMode spin *> (Join-Path $run 'transitions.log')
$transitionExit = $LASTEXITCODE
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'VerifyLinuxSampledSignals.ps1') -EvidenceDirectory (Join-Path $run 'sampled') -WaitMode spin -CompareGcModes -Cadence qualified-sample -Startup warm -Repetitions 3 *> (Join-Path $run 'sampled.log')
$sampledExit = $LASTEXITCODE
@{ transitionExitCode=$transitionExit; sampledExitCode=$sampledExit; passed=($transitionExit -eq 0 -and $sampledExit -eq 0) } | ConvertTo-Json | Set-Content (Join-Path $run 'summary.json')
Write-Host "Spin reliability evidence: $run"
if ($transitionExit -ne 0 -or $sampledExit -ne 0) { throw 'Spin reliability failed. Retain every outcome.' }
