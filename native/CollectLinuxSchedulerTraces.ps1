param([string]$EvidenceDirectory = (Join-Path $PSScriptRoot '../artifacts/scheduler-traces'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'CollectLinuxRuntimeTraces.ps1') -Scheduler -Repetitions 1 -EvidenceDirectory $EvidenceDirectory
