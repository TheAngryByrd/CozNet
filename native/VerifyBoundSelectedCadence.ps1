param([string]$EvidenceDirectory, [ValidateRange(1, 2147483647)][int]$SelectedBatches = 5000)
$ErrorActionPreference = 'Stop'
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $PSScriptRoot '../artifacts/bound-selected-cadence' }
$run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$order = @('continuous', 'qualified-sample', 'qualified-sample', 'continuous')
$outcomes = @()
for ($index = 0; $index -lt $order.Count; $index++) {
    $mode = $order[$index]
    $directory = Join-Path $run "$index-$mode"
    $failure = $null
    try {
        & (Join-Path $PSScriptRoot 'VerifyBoundSampledPayment.ps1') -SelectedPayment -SelectedCadence $mode -SelectedBatches $SelectedBatches -EvidenceDirectory $directory *> (Join-Path $run "$index-$mode.log")
    }
    catch { $failure = $_.Exception.ToString() }
    $summaries = @(Get-ChildItem -LiteralPath $directory -Filter 'summary.json' -Recurse -ErrorAction SilentlyContinue)
    $passed = $false
    if ($summaries.Count -eq 1) {
        try { $summary = Get-Content -LiteralPath $summaries[0].FullName -Raw | ConvertFrom-Json; $passed = $summary.passed -is [bool] -and $summary.passed }
        catch { $failure = $_.Exception.ToString() }
    }
    $outcomes += @{ index = $index; cadence = $mode; passed = $passed -and $null -eq $failure; failure = $failure; summaries = @($summaries | ForEach-Object FullName) }
}
$passed = @($outcomes | Where-Object { -not $_.passed }).Count -eq 0
@{ passed = $passed; requestedBatches = $SelectedBatches; order = $order; outcomes = $outcomes; predictionAccuracyEstablished = $false; observerCostIsolated = $false } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $run 'summary.json')
Write-Output $run
if (-not $passed) { throw 'The selected cadence comparison retains failed cases. Inspect every run.' }
