#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d "${TMPDIR:-/tmp}/lain-key-reader-tests.XXXXXX")"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/ReaderTests.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <Compile Include="$root/src/Lain.Core/*.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaContracts.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaHttp.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaService.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/KiloQuotaAdapter.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/KiloPassReader.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/RequestyQuotaAdapter.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/SyntheticQuotaAdapter.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/StepFunQuotaAdapter.cs" />
    <Compile Include="$root/src/Lain.Host/Providers/Xai/XaiKeyMetadataReader.cs" />
    <Compile Include="$root/tests/account-connect-readers/Program.cs" />
    <Compile Include="$root/tests/account-connect-readers/MetadataRetention.cs" />
  </ItemGroup>
</Project>
XML
dotnet build "$temp/ReaderTests.csproj" -r linux-x64 -m:1 -p:BuildInParallel=false -nr:false --verbosity quiet
dotnet run --project "$temp/ReaderTests.csproj" -r linux-x64 --no-build --no-restore --verbosity quiet
