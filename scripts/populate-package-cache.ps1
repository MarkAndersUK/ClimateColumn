<#
.SYNOPSIS
    Populates the local NuGet cache with the packages ClimateColumn.Tests needs, so that
    the solution restores, builds and tests entirely offline.

.DESCRIPTION
    nuget.config clears every package source, which is what keeps the build offline. That
    works from a cold cache for ClimateColumn.Core and ClimateColumn.Cli, which have no
    package dependencies at all, but not for ClimateColumn.Tests: MSTest has to come from
    somewhere the first time.

    This script does that one fetch, with the source supplied on the command line rather
    than in nuget.config, so the committed configuration stays offline-only. Once it has
    run, the packages live in the cache and nothing reaches the network again.

    Run it once per machine (or after clearing the cache). It is idempotent.

.PARAMETER Source
    Where to fetch from. Defaults to nuget.org. For an air-gapped machine, point this at a
    folder of .nupkg files copied from a machine that has them:

        ./scripts/populate-package-cache.ps1 -Source D:\offline-nupkgs

    Such a folder can be produced with -Export on a connected machine.

.PARAMETER Export
    Instead of only populating the cache, also copy the resolved .nupkg files into this
    folder, to carry to an air-gapped machine.

.EXAMPLE
    ./scripts/populate-package-cache.ps1
    Fetches from nuget.org into the local cache.

.EXAMPLE
    ./scripts/populate-package-cache.ps1 -Export ..\offline-nupkgs
    Fetches, then stages the .nupkg files for transfer.
#>
[CmdletBinding()]
param(
    [string] $Source = 'https://api.nuget.org/v3/index.json',
    [string] $Export
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# Restore the whole solution rather than one project, so every test project's packages land
# in the cache. Both test projects use the same MSTest versions, so this adds nothing to the
# download; it just stops a newly added project from being missed.
$testProject = Join-Path $repoRoot 'ClimateColumn.sln'

if (-not (Test-Path $testProject)) {
    throw "Could not find the solution at $testProject"
}

Write-Host "Populating the NuGet cache from: $Source"

# --source overrides the cleared sources in nuget.config for this one invocation only,
# which is the whole point: the committed configuration never gains a network source.
& dotnet restore $testProject --source $Source --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed. If this machine has no network access, re-run with -Source pointing at a folder of .nupkg files."
}

Write-Host ""
Write-Host "Cache populated. The solution now builds and tests offline:" -ForegroundColor Green
Write-Host "  dotnet build ClimateColumn.sln -c Release"
Write-Host "  dotnet test  ClimateColumn.sln -c Release"

if (-not $Export) { return }

# Read the exact resolved closure rather than guessing at transitive dependencies. Both test
# projects resolve the same MSTest packages, so either assets file gives the full set.
$assets = Join-Path $repoRoot 'tests\ClimateColumn.Tests\obj\project.assets.json'
if (-not (Test-Path $assets)) { throw "Expected $assets to exist after a successful restore." }

$packagesRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
New-Item -ItemType Directory -Force -Path $Export | Out-Null

$libraries = (Get-Content $assets -Raw | ConvertFrom-Json).libraries
$copied = 0
foreach ($entry in $libraries.PSObject.Properties) {
    if ($entry.Value.type -ne 'package') { continue }

    $id, $version = $entry.Name.Split('/')
    $nupkg = Join-Path $packagesRoot "$($id.ToLowerInvariant())\$version\$($id.ToLowerInvariant()).$version.nupkg"

    if (Test-Path $nupkg) {
        Copy-Item $nupkg -Destination $Export -Force
        $copied++
    }
    else {
        Write-Warning "Not found in the cache, skipped: $($entry.Name)"
    }
}

$size = (Get-ChildItem $Export -Filter *.nupkg | Measure-Object -Property Length -Sum).Sum
Write-Host ""
Write-Host ("Exported {0} packages ({1:N1} MB) to {2}" -f $copied, ($size / 1MB), (Resolve-Path $Export)) -ForegroundColor Green
Write-Host "Copy that folder to the offline machine and run:"
Write-Host "  ./scripts/populate-package-cache.ps1 -Source <folder>"
