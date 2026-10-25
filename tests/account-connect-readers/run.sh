#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
temp="$(mktemp -d "${TMPDIR:-/tmp}/lain-key-reader-tests.XXXXXX")"
trap 'rm -rf "$temp"' EXIT
cat > "$temp/ReaderTests.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RuntimeIdentifier>linux-x64</RuntimeIdentifier><SelfContained>true</SelfContained><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$root/src/Lain.Host/Lain.Host.csproj" />
    <Compile Include="$root/tests/account-connect-readers/Program.cs" />
    <Compile Include="$root/tests/account-connect-readers/MetadataRetention.cs" />
    <Compile Include="$root/tests/account-connect-readers/ReaderRecovery.cs" />
  </ItemGroup>
</Project>
XML
MSBUILDDISABLENODEREUSE=1 dotnet build "$temp/ReaderTests.csproj" -r linux-x64 --self-contained true -m:1 -p:BuildInParallel=false -p:RestoreLockedMode=true -nr:false --verbosity quiet
MSBUILDDISABLENODEREUSE=1 dotnet run --project "$temp/ReaderTests.csproj" -r linux-x64 --self-contained true --no-build --no-restore --verbosity quiet
