# Requires Python 3.10+ and an ordinary interactive Windows user session.
[CmdletBinding()]
param(
    [ValidateSet('check','install','uninstall')][string]$Action = 'check',
    [string]$Package
)
$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot '../packaging/platform-installers/install.py'
$arguments = @($engine, '--platform', 'windows', $Action)
if ($Package) { $arguments += $Package }
& python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
