<#
.SYNOPSIS
Builds the whole solution, tests included.
.DESCRIPTION
The control panel and the SFU are not built here; the Docker images are made by publish.ps1 next to
this script. No MSBuild node and no compiler server outlives the build.
.PARAMETER Configuration
Debug (the default) or Release.
.PARAMETER Arguments
Anything else goes to dotnet build as is.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\.."))

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK (dotnet) is not installed" }
# Windows PowerShell turns any stderr line of a native command into a terminating error under "Stop";
# the exit code is what tells whether the build failed
$ErrorActionPreference = "Continue"
dotnet build FlowRateServer.sln -c $Configuration -nologo -nodeReuse:false -p:UseSharedCompilation=false @Arguments
exit $LASTEXITCODE
