param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/native-linux'))

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$runtime = Join-Path $output 'runtime'
$work = Join-Path $output 'mapprofiler'
$generated = Join-Path $work 'generated'
$fixtures = Join-Path $root 'tests/CozNet.Tests/fixtures/map-capture'
$revision = '60629d14374c56f1cb51819049ad1fa529307f8d'
$image = 'coznet-native-build:10.0.100'

if (-not (Test-Path -LiteralPath (Join-Path $runtime '.git'))) { throw 'Run native/BuildLinux.ps1 first to obtain the pinned runtime sources.' }
$head = & git -C $runtime rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $revision) { throw 'The runtime checkout does not match the pinned revision.' }
$changes = & git -C $runtime status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'The runtime checkout must have no local changes.' }
& docker image inspect $image --format '{{.Id}}' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Run native/BuildLinux.ps1 first to build the Linux compiler image.' }

New-Item -ItemType Directory -Force $generated | Out-Null
& (Join-Path $PSScriptRoot 'GenerateBases.ps1') -RuntimeSource $runtime -OutputDirectory $generated

foreach ($variant in @(@('release', 'OFF'), @('thread', 'ON'))) {
    $build = Join-Path $work $variant[0]
    $written = Join-Path $build 'fixtures'
    if (Test-Path -LiteralPath $written) { Remove-Item -Recurse -Force $written }
    New-Item -ItemType Directory -Force $written | Out-Null
    $container = @('run', '--rm', '--pull', 'never', '--network', 'none', '--cap-drop', 'ALL', '--read-only',
        '--tmpfs', '/tmp:rw,nosuid,size=256m',
        '--mount', "type=bind,source=$root,target=/source,readonly",
        '--mount', "type=bind,source=$runtime,target=/runtime,readonly",
        '--mount', "type=bind,source=$generated,target=/generated,readonly",
        '--mount', "type=bind,source=$build,target=/build", $image)
    & docker @container cmake -S /source/native/mapprofiler -B /build -G Ninja -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_C_COMPILER=clang -DCMAKE_BUILD_TYPE=Release -DRUNTIME_SOURCE=/runtime -DGENERATED_INCLUDE=/generated "-DMAP_SANITIZE_THREAD=$($variant[1])"
    if ($LASTEXITCODE -ne 0) { throw "The $($variant[0]) configure step failed." }
    & docker @container cmake --build /build --parallel 1
    if ($LASTEXITCODE -ne 0) { throw "The $($variant[0]) compilation failed." }
    if ($variant[1] -eq 'OFF') {
        $exports = @(& docker @container nm -D --defined-only --format=just-symbols /build/libCozNet.MapProfiler.so | Sort-Object)
        if ($LASTEXITCODE -ne 0 -or ($exports -join ',') -ne ((@('DllGetClassObject', 'coz_map_read') | Sort-Object) -join ',')) { throw "The library exports $($exports -join ','), not only DllGetClassObject and coz_map_read." }
    }
    $commands = & docker @container ninja -C /build -t commands MapCaptureChecks $(if ($variant[1] -eq 'OFF') { 'libCozNet.MapProfiler.so' })
    if ($LASTEXITCODE -ne 0) { throw "Cannot list the $($variant[0]) build commands." }
    $evidence = @("revision $revision", "image $(& docker image inspect $image --format '{{.Id}}')") + @(& docker @container sh -c 'clang++ --version | head -1') + @('commands:') + $commands
    if ($variant[1] -eq 'ON') {
        foreach ($object in @('MapCaptureChecks.Linux.cpp.o', 'MapProfiler.Linux.cpp.o')) {
            $probes = @(& docker @container sh -c "nm -u /build/CMakeFiles/MapCaptureChecks.dir/$object | grep -c ' __tsan_'")
            if ($LASTEXITCODE -ne 0 -or [int]$probes[0] -le 0) { throw "The object $object has no ThreadSanitizer instrumentation." }
            $evidence += "$object references $($probes[0]) __tsan_ symbols"
        }
        $runtimeIdentity = @(& docker @container sh -c 'ldd /build/MapCaptureChecks | grep libtsan; sha256sum "$(readlink -f /usr/lib/gcc/x86_64-linux-gnu/13/libtsan.so)"; dpkg-query -W libtsan2')
        if ($LASTEXITCODE -ne 0 -or -not ($runtimeIdentity -match 'libtsan\.so\.2')) { throw 'The ThreadSanitizer checks do not load the GCC libtsan runtime.' }
        $evidence += @('runtime: clang 18 instrumentation with the GCC libtsan runtime of the same image') + $runtimeIdentity
        $race = @(& docker @container env 'TSAN_OPTIONS=halt_on_error=1 exitcode=66' /build/MapCaptureChecks --seeded-race 2>&1)
        if ($LASTEXITCODE -ne 66 -or -not ($race -match 'WARNING: ThreadSanitizer: data race')) { throw 'The seeded race was not reported, so the ThreadSanitizer run proves nothing.' }
        $evidence += 'seeded race: reported with exit code 66'
    }
    $evidence | Set-Content (Join-Path $build 'evidence.txt')
    & docker @container env 'TSAN_OPTIONS=halt_on_error=1 exitcode=66' /build/MapCaptureChecks /build/fixtures
    if ($LASTEXITCODE -ne 0) { throw "The $($variant[0]) map capture checks failed." }

    $expected = @(Get-ChildItem -LiteralPath $fixtures -Filter '*.bin' | Sort-Object Name)
    $actual = @(Get-ChildItem -LiteralPath $written -Filter '*.bin' | Sort-Object Name)
    if ($expected.Count -eq 0) { throw 'The committed map capture fixtures are missing.' }
    if (($expected.Name -join ',') -ne ($actual.Name -join ',')) { throw "The $($variant[0]) checks wrote $($actual.Name -join ','), but the fixtures are $($expected.Name -join ',')." }
    foreach ($file in $expected) {
        $left = [IO.File]::ReadAllBytes($file.FullName)
        $right = [IO.File]::ReadAllBytes((Join-Path $written $file.Name))
        if ([Convert]::ToBase64String($left) -ne [Convert]::ToBase64String($right)) { throw "The $($variant[0]) record $($file.Name) differs from the committed fixture." }
    }
    "$($variant[0]): $($expected.Count) fixtures are identical"
}
