#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d "${TMPDIR:-/tmp}/lain-monitor-auth-tests.XXXXXX")"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/MonitorAuthTests.csproj" <<PROJECT
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <Compile Include="$root/src/Lain.Core/*.cs" />
    <Compile Include="$root/src/Lain.Host/Accounts/Monitoring/*.cs" />
    <Compile Include="$root/src/Lain.Host/Operations/OwnStatePaths.cs" />
    <Compile Include="$root/src/Lain.Host/Operations/ConfigurationRepository.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaContracts.cs" />
    <Compile Include="$root/tests/monitor-auth/Program.cs" />
  </ItemGroup>
</Project>
PROJECT
dotnet build "$temp/MonitorAuthTests.csproj" -m:1 -p:BuildInParallel=false -nr:false --verbosity quiet
dotnet run --project "$temp/MonitorAuthTests.csproj" --no-build --no-restore --verbosity quiet
