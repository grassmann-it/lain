#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d /tmp/lain-quota-tests.XXXXXX)"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/QuotaTests.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><Compile Include="$root/src/Lain.Core/*.cs" /><Compile Include="$root/src/Lain.Host/Quota/*.cs" /><Compile Include="$root/tests/quota/Program.cs" /></ItemGroup>
</Project>
EOF
dotnet build "$temp/QuotaTests.csproj" -m:1 -p:BuildInParallel=false -nr:false --verbosity quiet
dotnet run --project "$temp/QuotaTests.csproj" --no-build --no-restore --verbosity quiet
