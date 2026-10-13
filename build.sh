#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet tool restore
FAKE_DETAILED_ERRORS=true dotnet run --project ./build/build.fsproj -- -t "$@"
