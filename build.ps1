#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes DOS Launch as both available distribution builds.

.DESCRIPTION
    Creates dist\framework-dependent (small, needs the .NET 10 Desktop Runtime) and
    dist\self-contained (single exe with the runtime inside, needs nothing installed), plus a zip archive
    of each. The architecture is set by RuntimeIdentifier in the publish profiles
    (src\DosLaunch\Properties\PublishProfiles).

.EXAMPLE
    .\build.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\DosLaunch\DosLaunch.csproj'
$dist = Join-Path $root 'dist'

$builds = [ordered]@{
    'framework-dependent' = 'FrameworkDependent-win-x64'
    'self-contained'      = 'SelfContained-win-x64'
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null

foreach ($name in $builds.Keys) {
    $output = Join-Path $dist $name
    if (Test-Path $output) {
        Remove-Item $output -Recurse -Force
    }

    Write-Host "Publishing the $name build to $output"
    & dotnet publish $project -c Release -p:PublishProfile=$($builds[$name]) -o $output --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing the $name build failed."
    }

    $archive = Join-Path $dist "$name.zip"
    if (Test-Path $archive) {
        Remove-Item $archive -Force
    }

    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive
}

Write-Host "Done. Builds and archives are in $dist"
