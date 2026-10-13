@echo off
cd /d "%~dp0"
dotnet tool restore || exit /b 1
dotnet run --project ./build/build.fsproj -- -t %*
