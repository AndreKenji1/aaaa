#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
dotnet restore HydroGuard.sln
dotnet build HydroGuard.sln -c Release --no-restore --disable-build-servers -m:1
dotnet test tests/HydroGuard.Tests/HydroGuard.Tests.csproj \
  -c Release --no-build --no-restore \
  --logger 'trx;LogFileName=tests.trx' \
  --results-directory TestResults \
  --collect:'XPlat Code Coverage'
