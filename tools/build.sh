#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
python3 "$repo_dir/tools/fetch-usb.py" --verify-only
cd "$repo_dir/source"
dotnet restore AutoTunel.csproj --disable-parallel
dotnet restore tests/Tests.csproj --disable-parallel
dotnet build AutoTunel.csproj --no-restore -c Release -o dist/AutoTunel -maxcpucount:2
dotnet build tests/Tests.csproj --no-restore -c Release -maxcpucount:2
dotnet run --project tests/Tests.csproj --no-build --no-restore -c Release
dotnet run --project tests/Tests.csproj --no-build --no-restore -c Release -- --verify-bundle "$repo_dir/source/dist/AutoTunel/AutoTunel.exe"
python3 "$repo_dir/tools/verify-icon.py" "$repo_dir/source/dist/AutoTunel/AutoTunel.exe" "$repo_dir/source/AutoTunel.ico"
