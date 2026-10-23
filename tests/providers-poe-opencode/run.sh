#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d /tmp/lain-poe-opencode-tests.XXXXXX)"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/ProviderTests.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <Compile Include="$root/src/Lain.Core/*.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaContracts.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaHttp.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/PoeQuotaAdapter.cs" />
    <Compile Include="$root/src/Lain.Host/Providers/Poe/*.cs" />
    <Compile Include="$root/src/Lain.Host/Providers/OpenCode/*.cs" />
    <Compile Include="$root/tests/providers-poe-opencode/Program.cs" />
  </ItemGroup>
</Project>
EOF
dotnet build "$temp/ProviderTests.csproj" -m:1 -p:BuildInParallel=false -nr:false --verbosity quiet
dotnet run --project "$temp/ProviderTests.csproj" --no-build --no-restore --verbosity quiet
