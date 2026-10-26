<#
.SYNOPSIS
Builds the Docker images of the server (with the control panel) and the SFU and packs them for deployment.
.DESCRIPTION
The archive dist\FlowRateServer-<version>-linux-<arch>-DOCKER.tar.gz holds one FlowRateServer-<version>
folder with everything a machine needs to run the server: the images, the compose files without their
build sections, an example .env, the installer and a README. There, inside the unpacked folder:
sh install.sh
The images are named after the release (ghcr.io/puffer-bit/flowrate-server:<release without v>). The same
files without the images go to dist\install\, the release assets install.sh downloads when it runs without
the archive. RELEASE_TAG names the release (v<version> by default), IMAGE_REGISTRY the registry.
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
$release = if ($env:RELEASE_TAG) { $env:RELEASE_TAG } else { "v$version" }
$registry = if ($env:IMAGE_REGISTRY) { $env:IMAGE_REGISTRY } else { "ghcr.io/puffer-bit" }
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
    $sources = @(docker compose config --images)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    # flowrate/server:latest becomes $registry/flowrate-server:<release>, in the images and in the compose file
    $rename = @{}
    $images = foreach ($image in $sources) {
        $target = "$registry/$(($image -replace ':[^:/]*$', '') -replace '/', '-'):$($release -replace '^v', '')"
        docker tag $image $target
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $rename["image: $image"] = "image: $target"
        $target
    }
    docker save -o (Join-Path $staging "images.tar") @images
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # The build sections point into src\, which the target machine does not have
    $skip = $false
    $compose = foreach ($line in Get-Content docker-compose.yml) {
        if ($line -match '^    build:') { $skip = $true; continue }
        if ($skip -and $line -match '^      ') { continue }
        $skip = $false
        if ($rename.ContainsKey($line.Trim())) { $line = $line.Replace($line.Trim(), $rename[$line.Trim()]) }
        $line
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $staging "docker-compose.yml"), ($compose -join "`n") + "`n", $utf8)
    Copy-Item docker-compose.bridge.yml, (Join-Path $package "README.txt") $staging
    # LF line endings whatever git checked out: sh fails on CR
    $installer = Get-Content (Join-Path $package "install.sh") | ForEach-Object {
        if ($_ -eq "release=") { "release=$release" } else { $_ }
    }
    [System.IO.File]::WriteAllText((Join-Path $staging "install.sh"), ($installer -join "`n") + "`n", $utf8)
    # Not .env: unpacking a newer archive over an installed one must not replace the settings there
    $envFile = Get-Content (Join-Path $package "env")
    [System.IO.File]::WriteAllText((Join-Path $staging ".env.example"), ($envFile -join "`n") + "`n", $utf8)

    New-Item -ItemType Directory -Force dist | Out-Null
    tar -czf $archive -C $root $name
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (Test-Path dist\install) { Remove-Item -Recurse -Force dist\install }
    New-Item -ItemType Directory dist\install | Out-Null
    Copy-Item ("docker-compose.yml", "docker-compose.bridge.yml", "install.sh", "README.txt" | ForEach-Object { Join-Path $staging $_ }) dist\install
    # GitHub drops the leading dot of asset names
    Copy-Item (Join-Path $staging ".env.example") dist\install\env.example
    Write-Host "Images:  $($images -join ' ')"
    Write-Host "Archive: $archive ($([math]::Round((Get-Item $archive).Length / 1MB)) MiB)"
}
finally {
    Remove-Item -Recurse -Force $root
}
