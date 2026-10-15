function Read-BoundSampledOutcome {
    param([string]$Path, [string]$Case, [int]$ExitCode,
        [ValidateSet('', 'continuous', 'qualified-sample')][string]$SelectedCadence = '',
        [int]$SelectedBatches = 5000, [Nullable[bool]]$ExpectedPauseTiming = $null, [Nullable[bool]]$ExpectedChildCpu = $null, [Nullable[bool]]$ExpectedOwnerDrain = $null,
        [Nullable[int]]$ExpectedReceiptCaptureVersion = $null, [Nullable[bool]]$ExpectedBatchService = $null, [Nullable[bool]]$ExpectedDispatchTiming = $null, [Nullable[bool]]$ExpectedDirectReceiptReady = $null)
    $passed = $false
    $recordError = $null
    try {
        $record = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($record.passed -isnot [bool]) { throw 'The case record lacks a Boolean outcome.' }
        if ($null -ne $ExpectedDirectReceiptReady -and ($record.directReceiptReadyEnabled -isnot [bool] -or $record.directReceiptReadyEnabled -ne $ExpectedDirectReceiptReady)) {
            throw 'Receipt readiness evidence differs from the requested setting.'
        }
        if ($null -ne $ExpectedDispatchTiming -and ($record.dispatchTimingEnabled -isnot [bool] -or $record.dispatchTimingEnabled -ne $ExpectedDispatchTiming)) { throw 'Dispatch timing evidence differs from the requested setting.' }
        if ($null -ne $ExpectedBatchService -and ($record.batchServiceEnabled -isnot [bool] -or $record.batchServiceEnabled -ne $ExpectedBatchService)) { throw 'Batch service evidence differs from the requested setting.' }
        if ($null -ne $ExpectedReceiptCaptureVersion -and (($record.receiptIntervalCaptureVersion -isnot [int] -and $record.receiptIntervalCaptureVersion -isnot [long]) -or $record.receiptIntervalCaptureVersion -ne $ExpectedReceiptCaptureVersion)) { throw 'Receipt capture version differs from the requested version.' }
        if ($null -ne $ExpectedPauseTiming -and ($record.pauseTimingEnabled -isnot [bool] -or $record.pauseTimingEnabled -ne $ExpectedPauseTiming)) {
            throw 'Pause timing evidence differs from the requested setting.'
        }
        if ($null -ne $ExpectedChildCpu -and ($record.childCpuEnabled -isnot [bool] -or $record.childCpuEnabled -ne $ExpectedChildCpu)) {
            throw 'Child CPU evidence differs from the requested setting.'
        }
        if ($null -ne $ExpectedOwnerDrain -and ($record.ownerDrainEnabled -isnot [bool] -or $record.ownerDrainEnabled -ne $ExpectedOwnerDrain)) { throw 'Owner drain evidence differs from the requested setting.' }
        if ($SelectedCadence) {
            $expectedMode = if ($SelectedCadence -eq 'continuous') { 'Continuous' } else { 'QualifiedSample' }
            $integerSchema = $record.schemaVersion -is [int] -or $record.schemaVersion -is [long]
            $integerBatches = $record.requestedBatches -is [int] -or $record.requestedBatches -is [long]
            if (-not $integerSchema -or $record.schemaVersion -ne 4 -or $record.selectedCadence -cne $expectedMode -or -not $integerBatches -or $record.requestedBatches -ne $SelectedBatches) {
                throw 'Cadence evidence differs from requested settings.'
            }
        }
        $passed = $ExitCode -eq 0 -and $record.passed
    }
    catch { $recordError = $_.Exception.Message }
    return @{ case = $Case; exitCode = $ExitCode; passed = $passed; recordError = $recordError }
}
