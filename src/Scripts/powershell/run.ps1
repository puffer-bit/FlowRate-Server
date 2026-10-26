<#
.SYNOPSIS
Builds the server and starts it.
.DESCRIPTION
The server runs from Server\Server.Asp, as the IDE does. The SFU is not started here: run it from
Sfu\mediasoup (npm run dev) or with docker compose.
.PARAMETER Configuration
Debug (the default) or Release.
.PARAMETER Panel
Build the control panel first (npm ci on the first run).
.PARAMETER Arguments
Anything else goes to the server.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File run.ps1
powershell -ExecutionPolicy Bypass -File run.ps1 -Configuration Release -Panel
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$Panel,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\.."))
$project = "Server\Server.Asp"
$panelPath = "Web\admin-panel"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK (dotnet) is not installed" }
if ($Panel -and -not (Get-Command npm -ErrorAction SilentlyContinue)) { throw "npm is not installed" }

# Windows PowerShell turns any stderr line of a native command into a terminating error under "Stop";
# the exit code is what tells whether the build failed
$ErrorActionPreference = "Continue"
if ($Panel) {
    if (-not (Test-Path (Join-Path $panelPath "node_modules"))) {
        npm --prefix $panelPath ci
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    npm --prefix $panelPath run build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
dotnet build $project -c $Configuration -nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Set-Location $project
dotnet run -c $Configuration --no-build -- @Arguments
exit $LASTEXITCODE
