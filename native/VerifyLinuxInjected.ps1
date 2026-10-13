param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-injected'))

$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
foreach ($mode in @('manual', 'injected')) {
    & (Join-Path $PSScriptRoot 'VerifyLinuxPerf.ps1') -ProbeMode $mode -WaitBackend linux-nanosleep -EvidenceDirectory (Join-Path $run $mode)
}
$manual = Get-Content (Join-Path $run 'manual/control.json') -Raw | ConvertFrom-Json
$injected = Get-Content (Join-Path $run 'injected/control.json') -Raw | ConvertFrom-Json
$manualCost = $manual.observations[0].experimentMilliseconds / $manual.completedBatches
$injectedCost = $injected.observations[0].experimentMilliseconds / $injected.completedBatches
@{ manualMillisecondsPerBatch = $manualCost; injectedMillisecondsPerBatch = $injectedCost;
    costRatio = $injectedCost / $manualCost; trialCount = 1; order = @('manual', 'injected');
    predictionAccuracyEstablished = $false } | ConvertTo-Json | Set-Content (Join-Path $run 'comparison.json')
