#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d /tmp/lain-synthetic-tests.XXXXXX)"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/Tests.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><Compile Include="$root/src/Lain.Core/*.cs" /><Compile Include="$root/src/Lain.Host/Providers/SyntheticXaiConfiguration.cs" /><Compile Include="$root/src/Lain.Host/Quota/QuotaContracts.cs" /><Compile Include="$root/src/Lain.Host/Quota/QuotaHttp.cs" /><Compile Include="$root/src/Lain.Host/Quota/SyntheticQuotaAdapter.cs" /><Compile Include="$root/tests/providers-synthetic-xai/Program.cs" /></ItemGroup>
</Project>
EOF
dotnet run --project "$temp/Tests.csproj" --verbosity quiet -- "$root/src/Lain.Host/Providers/Catalog"
