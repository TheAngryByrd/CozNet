function Read-BoundSampledOutcome {
    param([string]$Path, [string]$Case, [int]$ExitCode,
        [ValidateSet('', 'continuous', 'qualified-sample')][string]$SelectedCadence = '',
        [int]$SelectedBatches = 5000, [Nullable[bool]]$ExpectedPauseTiming = $null, [Nullable[bool]]$ExpectedChildCpu = $null, [Nullable[bool]]$ExpectedOwnerDrain = $null,
        [Nullable[int]]$ExpectedReceiptCaptureVersion = $null, [Nullable[bool]]$ExpectedBatchService = $null, [Nullable[bool]]$ExpectedDispatchTiming = $null, [Nullable[bool]]$ExpectedDirectReceiptReady = $null, [Nullable[bool]]$ExpectedPeerProgress = $null, [Nullable[bool]]$ExpectedScheduler = $null, [Nullable[bool]]$ExpectedSelectedHandoff = $null, [Nullable[bool]]$ExpectedFiniteMeasurement = $null, [Nullable[bool]]$ExpectedOwnerController = $null, [Nullable[int]]$ExpectedProbeInterval = $null, [Nullable[bool]]$ExpectedReceiptPreparation = $null)
    $passed = $false
    $recordError = $null
    try {
        $record = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($null -ne $ExpectedReceiptPreparation -and ($record.receiptPreparationEnabled -isnot [bool] -or $record.receiptPreparationEnabled -ne $ExpectedReceiptPreparation)) {
            throw 'Receipt preparation evidence differs from the requested setting.'
        }
        if ($null -ne $ExpectedOwnerController -and ($record.ownerControllerEnabled -isnot [bool] -or $record.ownerControllerEnabled -ne $ExpectedOwnerController)) {
            throw 'Owner controller evidence differs from the requested setting.'
        }
        if ($null -ne $ExpectedProbeInterval -and (($record.probeInterval -isnot [int] -and $record.probeInterval -isnot [long]) -or $record.probeInterval -ne $ExpectedProbeInterval)) { throw 'Probe interval evidence differs from the requested setting.' }
        if ($record.passed -isnot [bool]) { throw 'The case record lacks a Boolean outcome.' }
        if ($null -ne $ExpectedFiniteMeasurement -and ($record.finiteMeasurementEnabled -isnot [bool] -or $record.finiteMeasurementEnabled -ne $ExpectedFiniteMeasurement)) { throw 'Finite measurement evidence differs from the requested setting.' }
        if ($null -ne $ExpectedSelectedHandoff -and ($record.selectedHandoffEnabled -isnot [bool] -or $record.selectedHandoffEnabled -ne $ExpectedSelectedHandoff)) { throw 'Selected handoff evidence differs from the requested setting.' }
        if ($null -ne $ExpectedScheduler -and ($record.schedulerTiming -isnot [bool] -or $record.schedulerTiming -ne $ExpectedScheduler)) { throw 'Scheduler evidence differs from the requested setting.' }
        if ($null -ne $ExpectedPeerProgress -and ($record.peerProgressEnabled -isnot [bool] -or $record.peerProgressEnabled -ne $ExpectedPeerProgress)) { throw 'Peer progress evidence differs from the requested setting.' }
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
