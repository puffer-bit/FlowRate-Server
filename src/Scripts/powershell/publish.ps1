<#
.SYNOPSIS
Builds the Docker images of the server (with the control panel) and the SFU and packs them for deployment.
.DESCRIPTION
The archive dist\FlowRateServer-<version>-linux-<arch>-DOCKER.tar.gz holds one FlowRateServer-<version>
folder with everything a machine needs to run the server: the images, the compose files without their
build sections, an example .env, the installer and a README. There, inside the unpacked folder:
sh install.sh
.PARAMETER Arguments
Anything goes to docker compose build as is.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File publish.ps1
powershell -ExecutionPolicy Bypass -File publish.ps1 --no-cache
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\..\.."))
$project = "src\Server\Server.Asp\Server.Asp.csproj"
$package = "src\Scripts\package"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker is not installed" }
# PUBLIC_IP matters only when the stack runs, compose demands it for any command
if (-not $env:PUBLIC_IP) { $env:PUBLIC_IP = "127.0.0.1" }

# Windows PowerShell turns any stderr line of a native command into a terminating error under "Stop";
# the exit code is what tells whether the build failed
$ErrorActionPreference = "Continue"
docker compose build @Arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$version = ([xml](Get-Content $project)).Project.PropertyGroup | ForEach-Object { $_.SelectSingleNode("InformationalVersion") } |
    Where-Object { $_ } | Select-Object -First 1 | ForEach-Object { $_.InnerText }
if (-not $version) { throw "No <InformationalVersion> in $project" }
# The images are built for the Docker host: amd64 becomes x64 as in the client archives
$arch = docker image inspect -f "{{.Architecture}}" flowrate/server:latest
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($arch -eq "amd64") { $arch = "x64" }
$name = "FlowRateServer-$version"
$archive = "dist\$name-linux-$arch-DOCKER.tar.gz"

$root = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
$staging = Join-Path $root $name
New-Item -ItemType Directory $staging | Out-Null
try {
    $images = @(docker compose config --images)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    docker save -o (Join-Path $staging "images.tar") @images
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # The build sections point into src\, which the target machine does not have
    $skip = $false
    $compose = foreach ($line in Get-Content docker-compose.yml) {
        if ($line -match '^    build:') { $skip = $true; continue }
        if ($skip -and $line -match '^      ') { continue }
        $skip = $false
        $line
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $staging "docker-compose.yml"), ($compose -join "`n") + "`n", $utf8)
    Copy-Item docker-compose.bridge.yml, (Join-Path $package "README.txt") $staging
    # LF line endings whatever git checked out: sh fails on CR
    $installer = Get-Content (Join-Path $package "install.sh")
    [System.IO.File]::WriteAllText((Join-Path $staging "install.sh"), ($installer -join "`n") + "`n", $utf8)
    # Not .env: unpacking a newer archive over an installed one must not replace the settings there
    $envFile = Get-Content (Join-Path $package "env")
    [System.IO.File]::WriteAllText((Join-Path $staging ".env.example"), ($envFile -join "`n") + "`n", $utf8)

    New-Item -ItemType Directory -Force dist | Out-Null
    tar -czf $archive -C $root $name
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Archive: $archive ($([math]::Round((Get-Item $archive).Length / 1MB)) MiB)"
}
finally {
    Remove-Item -Recurse -Force $root
}
