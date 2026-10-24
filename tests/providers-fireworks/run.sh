#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d "${TMPDIR:-/tmp}/lain-fireworks-tests.XXXXXX")"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/FireworksTests.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <Compile Include="$root/src/Lain.Core/*.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaContracts.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaHttp.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaService.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/FireworksQuotaAdapter.cs" />
    <Compile Include="$root/src/Lain.Host/Providers/ProviderCatalogRegistry.cs" />
    <Compile Include="$root/tests/providers-fireworks/Program.cs" />
  </ItemGroup>
</Project>
XML
dotnet build "$temp/FireworksTests.csproj" -m:1 -p:BuildInParallel=false -nr:false --verbosity quiet
dotnet run --project "$temp/FireworksTests.csproj" --no-build --no-restore --verbosity quiet -- "$root"
