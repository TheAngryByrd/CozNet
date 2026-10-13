FROM mcr.microsoft.com/dotnet/sdk:10.0.100
RUN apt-get update && apt-get install -y --no-install-recommends clang cmake ninja-build
