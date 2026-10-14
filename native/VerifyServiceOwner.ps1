param(
    [ValidateSet('Host', 'LinuxContainer')][string]$Platform = 'Host',
    [switch]$LinuxSignal,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($LinuxSignal -and $Platform -eq 'Host' -and -not $IsLinux) {
    throw 'The signal transport requires a Linux x64 host or LinuxContainer.'
}
$signalOption = if ($LinuxSignal) { 'ON' } else { 'OFF' }
if (-not $OutputDirectory) {
    $suffix = if ($LinuxSignal) { "-signal-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')" } else { '' }
    $OutputDirectory = Join-Path $root "artifacts/service-owner-$($Platform.ToLowerInvariant())$suffix"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $output | Out-Null

if ($Platform -eq 'LinuxContainer') {
    $image = 'coznet-native-build:10.0.100'
    $container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only', '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly", '--mount', "type=bind,source=$output,target=/build", $image)
    & docker @container cmake -S /source/native/service -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release "-DCOZ_SERVICE_LINUX_SIGNAL=$signalOption"
    if ($LASTEXITCODE -ne 0) { throw 'Service owner configuration failed.' }
    & docker @container cmake --build /build
    if ($LASTEXITCODE -ne 0) { throw 'Service owner compilation failed.' }
    & docker @container ctest --test-dir /build --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Service owner checks failed.' }
    return
}

$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
$cmake = if ($cmakeCommand) { $cmakeCommand.Source } else { $null }
if (-not $cmake -and $IsWindows) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.CMake.Project -property installationPath
        if ($visualStudio) {
            $cmake = Join-Path $visualStudio 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
        }
    }
}
if (-not $cmake -or -not (Test-Path -LiteralPath $cmake)) {
    throw 'Install CMake 3.20 or later and a C++20 compiler.'
}
$ctest = Join-Path (Split-Path $cmake) $(if ($IsWindows) { 'ctest.exe' } else { 'ctest' })
& $cmake -S (Join-Path $PSScriptRoot 'service') -B $output -DCMAKE_BUILD_TYPE=Release "-DCOZ_SERVICE_LINUX_SIGNAL=$signalOption"
if ($LASTEXITCODE -ne 0) { throw 'Service owner configuration failed.' }
& $cmake --build $output --config Release
if ($LASTEXITCODE -ne 0) { throw 'Service owner compilation failed.' }
& $ctest --test-dir $output -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) { throw 'Service owner checks failed.' }
