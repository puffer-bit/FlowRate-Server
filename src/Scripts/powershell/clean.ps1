<#
.SYNOPSIS
Removes everything dotnet builds: the bin\ and obj\ folders of every project in src\.
.DESCRIPTION
Stops the build servers first, they keep files of obj\ open. The next build starts with a full restore.
Only folders next to a .csproj are removed: node_modules\ of the panel and the SFU stay as they are,
and so do the server data and dist\.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File clean.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\.."))

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $ErrorActionPreference = "Continue"
    dotnet build-server shutdown 2>&1 | Out-Null
    $ErrorActionPreference = "Stop"
}

$count = 0
$projects = Get-ChildItem -Recurse -Filter *.csproj | Where-Object { $_.FullName -notmatch '[\\/]node_modules[\\/]' }
foreach ($project in $projects) {
    foreach ($name in "bin", "obj") {
        $folder = Join-Path $project.DirectoryName $name
        if (-not (Test-Path $folder)) { continue }
        Remove-Item -Recurse -Force $folder
        $count++
    }
}
Write-Host "Removed $count folders"
