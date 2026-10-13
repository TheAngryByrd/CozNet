param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/linux-countdown'))

$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$run = Join-Path $evidence ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $run | Out-Null
[IO.File]::WriteAllText((Join-Path $evidence 'last-run.txt'), $run)
$seed = 20261014
$random = [Random]::new($seed)
$outcomes = [Collections.Generic.List[object]]::new()
$costs = [Collections.Generic.List[object]]::new()
foreach ($block in 1..3) {
    $cases = @(
        @{ label = 'manual'; mode = 'manual'; interval = 1 },
        @{ label = 'dense'; mode = 'injected'; interval = 1 },
        @{ label = 'countdown-64'; mode = 'injected'; interval = 64 },
        @{ label = 'countdown-256'; mode = 'injected'; interval = 256 },
        @{ label = 'countdown-1024'; mode = 'injected'; interval = 1024 }
    )
    for ($i = $cases.Length - 1; $i -gt 0; $i--) {
        $j = $random.Next($i + 1)
        $cases[$i], $cases[$j] = $cases[$j], $cases[$i]
    }
    foreach ($case in $cases) {
        $relative = "block-$block/$($case.label)"
        $directory = Join-Path $run $relative
        $succeeded = $false
        $failure = $null
        try {
            & (Join-Path $PSScriptRoot 'VerifyLinuxPerf.ps1') -ProbeMode $case.mode -ProbeInterval $case.interval -WaitBackend linux-nanosleep -EvidenceDirectory $directory
            $succeeded = $true
        } catch {
            $failure = $_.Exception.Message
        }
        $outcomes.Add(@{ block = $block; label = $case.label; directory = $relative; succeeded = $succeeded; failure = $failure })
        if ($succeeded) {
            $control = Get-Content (Join-Path $directory 'control.json') -Raw | ConvertFrom-Json
            $active = $control.observations[0]
            $costs.Add(@{ block = $block; label = $case.label; millisecondsPerBatch = $active.experimentMilliseconds / $control.completedBatches;
                cpuNanosecondsPerBatch = $active.cpuNanoseconds / $control.completedBatches; completedBatches = $control.completedBatches;
                selectedSamples = $active.records.selectedMethodSamples; totalSamples = $active.records.samples;
                unmappedSamples = @($active.delivered | Where-Object { $null -eq $_.method }).Count })
        }
        @{ seed = $seed; blockCount = 3; outcomes = $outcomes.ToArray(); costs = $costs.ToArray();
            profilerCpuExclusionEstablished = $false; predictionAccuracyEstablished = $false } |
            ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run 'comparison.json')
    }
}
$ratios = foreach ($block in 1..3) {
    $manual = @($costs | Where-Object { $_.block -eq $block -and $_.label -eq 'manual' })
    if ($manual.Count -ne 1) { continue }
    foreach ($cost in @($costs | Where-Object { $_.block -eq $block -and $_.label -ne 'manual' })) {
        @{ block = $block; label = $cost.label; costRatio = $cost.millisecondsPerBatch / $manual[0].millisecondsPerBatch }
    }
}
@{ seed = $seed; blockCount = 3; outcomes = $outcomes.ToArray(); costs = $costs.ToArray(); ratios = @($ratios);
    profilerCpuExclusionEstablished = $false; predictionAccuracyEstablished = $false } |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run 'comparison.json')
if (@($outcomes | Where-Object { -not $_.succeeded }).Count -ne 0) {
    throw 'A probe comparison case failed. Inspect comparison.json and its retained records.'
}
