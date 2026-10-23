#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d /tmp/lain-nvidia-step-tests.XXXXXX)"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/ProviderTests.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <Compile Include="$root/src/Lain.Core/*.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaContracts.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/QuotaHttp.cs" />
    <Compile Include="$root/src/Lain.Host/Quota/StepFunQuotaAdapter.cs" />
    <Compile Include="$root/src/Lain.Host/Providers/NvidiaStep/NvidiaStepConfiguration.cs" />
    <Compile Include="$root/tests/providers-nvidia-step/Program.cs" />
  </ItemGroup>
</Project>
XML
dotnet run --project "$temp/ProviderTests.csproj" --verbosity quiet -- "$root"
