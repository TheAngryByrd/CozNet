param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/native-signal'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $output | Out-Null
$image = 'coznet-native-build:10.0.100'
& docker image inspect $image --format '{{.Id}}' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Build the native compiler image with NativeLinuxBuild first.' }
$container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
    '--tmpfs', '/tmp:rw,nosuid,size=64m', '--mount', "type=bind,source=$root,target=/source,readonly",
    '--mount', "type=bind,source=$output,target=/build", $image)
& docker @container cmake -S /source/native/signal -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Release
if ($LASTEXITCODE -ne 0) { throw 'Signal configuration failed.' }
& docker @container cmake --build /build
if ($LASTEXITCODE -ne 0) { throw 'Signal compilation failed.' }
& docker @container objdump -d -C /build/libCozNet.Signal.so | Set-Content (Join-Path $output 'disassembly.txt')
if ($LASTEXITCODE -ne 0) { throw 'Signal disassembly failed.' }
$disassembly = Get-Content (Join-Path $output 'disassembly.txt') -Raw
$handler = [regex]::Match($disassembly, '(?ms)^[0-9a-f]+ <\(anonymous namespace\)::handler\(int, siginfo_t\*, void\*\)>:\r?\n(?<body>.*?)(?=^[0-9a-f]+ <|\z)')
if (-not $handler.Success) { throw 'Cannot locate the compiled signal handler.' }
$body = $handler.Groups['body'].Value
[IO.File]::WriteAllText((Join-Path $output 'handler-disassembly.txt'), $body)
if ($body -match '\bcallq?\b|%[fg]s:|@plt' -or $body -notmatch '\bsyscall\b') {
    throw 'The compiled signal handler contains an unexpected call or TLS access, or lacks direct syscalls.'
}
