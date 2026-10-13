param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/native-linux'))

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$runtime = Join-Path $output 'runtime'
$generated = Join-Path $output 'generated'
$build = Join-Path $output 'integrated'
$revision = '60629d14374c56f1cb51819049ad1fa529307f8d'
$image = 'coznet-native-build:10.0.100'

if (-not (Test-Path -LiteralPath (Join-Path $runtime '.git'))) {
    & git clone --filter=blob:none --no-checkout --depth 1 --branch v10.0.0 https://github.com/dotnet/runtime.git $runtime
    if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain the pinned runtime sources.' }
    & git -C $runtime sparse-checkout set src/coreclr/inc src/coreclr/pal/inc src/coreclr/pal/prebuilt/inc src/tests/profiler/native src/native eng/native
    if ($LASTEXITCODE -ne 0) { throw 'Cannot configure the runtime source checkout.' }
    & git -C $runtime checkout --detach $revision
    if ($LASTEXITCODE -ne 0) { throw 'Cannot select the pinned runtime revision.' }
}
$head = & git -C $runtime rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $revision) { throw 'The runtime checkout does not match the pinned revision.' }
$changes = & git -C $runtime status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'The runtime checkout must have no local changes.' }

New-Item -ItemType Directory -Force $generated, $build | Out-Null
& (Join-Path $PSScriptRoot 'GenerateCallbacks.ps1') -HeaderPath (Join-Path $runtime 'src/coreclr/pal/prebuilt/inc/corprof.h') -OutputPath (Join-Path $generated 'CallbackBase.h')
$verificationImage = 'mcr.microsoft.com/dotnet/sdk:10.0.100'
& docker image inspect $verificationImage --format '{{.Id}}' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    & docker pull $verificationImage
    if ($LASTEXITCODE -ne 0) { throw 'Cannot obtain the Linux verification image.' }
}
& docker build --pull=false -t $image -f (Join-Path $PSScriptRoot 'linux.Dockerfile') $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'The Linux compiler image build failed.' }
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=256m',
    '--mount', "type=bind,source=$root,target=/source,readonly",
    '--mount', "type=bind,source=$runtime,target=/runtime,readonly",
    '--mount', "type=bind,source=$generated,target=/generated,readonly",
    '--mount', "type=bind,source=$build,target=/build", $image)
& docker @container cmake -S /source/native -B /build -G Ninja -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release -DRUNTIME_SOURCE=/runtime -DGENERATED_INCLUDE=/generated
if ($LASTEXITCODE -ne 0) { throw 'The Linux native configure step failed.' }
& docker @container cmake --build /build --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'The Linux native compilation failed.' }
