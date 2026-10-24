#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d "${TMPDIR:-/tmp}/lain-quota-tests.XXXXXX")"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/QuotaTests.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RuntimeIdentifier>linux-x64</RuntimeIdentifier><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup>
  <ItemGroup><ProjectReference Include="$root/src/Lain.Host/Lain.Host.csproj" /><Compile Include="$root/tests/quota/Program.cs" /></ItemGroup>
</Project>
EOF
MSBUILDDISABLENODEREUSE=1 dotnet build "$temp/QuotaTests.csproj" -r linux-x64 -m:1 -p:BuildInParallel=false -p:RestoreLockedMode=true -nr:false --verbosity quiet
MSBUILDDISABLENODEREUSE=1 dotnet run --project "$temp/QuotaTests.csproj" -r linux-x64 --no-build --no-restore --verbosity quiet
