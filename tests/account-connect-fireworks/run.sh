#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/lain-fireworks-tests.XXXXXX")"
trap 'rm -rf "$task_temp"' EXIT
cat > "$task_temp/Qualification.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RuntimeIdentifier>linux-x64</RuntimeIdentifier><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup>
<ItemGroup><ProjectReference Include="$root/src/Lain.Host/Lain.Host.csproj"/><Compile Include="$root/tests/account-connect-fireworks/Program.cs"/></ItemGroup>
</Project>
XML
MSBUILDDISABLENODEREUSE=1 dotnet build "$task_temp/Qualification.csproj" -r linux-x64 -m:1 -p:BuildInParallel=false -p:RestoreLockedMode=true -nr:false --verbosity quiet
MSBUILDDISABLENODEREUSE=1 dotnet run --project "$task_temp/Qualification.csproj" -r linux-x64 --no-build --no-restore --verbosity quiet
