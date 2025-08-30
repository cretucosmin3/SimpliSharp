#!/bin/bash
set -e

# Navigate to the root of the repository
cd "$(dirname "$0")/.."

# Build the project in Release mode
dotnet build src/SimpliSharp/SimpliSharp.csproj -c Release

# Pack the library in Release mode, without building again
dotnet pack src/SimpliSharp/SimpliSharp.csproj -c Release --no-build
