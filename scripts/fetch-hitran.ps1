<#
.SYNOPSIS
    Downloads a HITRAN line list so the band approximations can be checked against real
    spectral data.

.DESCRIPTION
    The line-by-line reference in ClimateColumn.Core works from any line list. Without one it
    uses a synthetic band, which validates the method but says nothing about whether the model
    resembles a real gas. This fetches the CO2 15 um band - the transition that does the actual
    greenhouse work - from hitran.org.

    The data is NOT committed. It is third-party data with its own citation requirement, and
    leaving it out keeps the repository buildable and testable with no network and no external
    files: the tests that want real lines skip when it is missing rather than failing. That is
    also why data/ is covered by the .gitignore *.csv rule.

    No API key is needed. hitran.org's line-by-line endpoint serves this request anonymously;
    an API key is only required for the HAPI2 client library. There is a daily request limit,
    so avoid re-running this in a loop.

.PARAMETER Molecule
    Which band to fetch. "co2-15um" (the default) is the 667 cm^-1 bending band of the two most
    abundant CO2 isotopologues. "h2o-rotational" is the far-infrared water band, useful for
    looking at a very different line structure.

.PARAMETER OutputDirectory
    Where to write. Defaults to data/ beside the solution.

.EXAMPLE
    ./scripts/fetch-hitran.ps1
    Fetches the CO2 15 um band to data/hitran-co2-15um.csv

.NOTES
    HITRAN data must be cited when used in published work:

      I.E. Gordon et al., "The HITRAN2020 molecular spectroscopic database",
      J. Quant. Spectrosc. Radiat. Transf. 277, 107949 (2022).

    See https://hitran.org for terms of use.
#>
[CmdletBinding()]
param(
    [ValidateSet('co2-15um', 'h2o-rotational', 'h2o-bending', 'o3-9.6um', 'ch4-7.7um', 'n2o-7.8um', 'all')]
    [string] $Molecule = 'co2-15um',

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'data' }

# Global isotopologue ids as used by hitran.org, taken from HAPI's own table rather than from
# memory: H2O 1-3, CO2 7-8, O3 16-18, N2O 21, CH4 32-33.
#
# Between them these cover 100-2000 cm^-1, which is where essentially all of a 200-300 K
# atmosphere's outgoing longwave sits. The more of that range is described by real lines, the less
# is left to the remainder band's single free number.
$bands = [ordered]@{
    'h2o-rotational' = @{ Isotopologues = '1,2,3';  From = 100;  To = 600;  File = 'hitran-h2o-rot.csv' }
    'co2-15um'       = @{ Isotopologues = '7,8';    From = 580;  To = 760;  File = 'hitran-co2-15um.csv' }
    'o3-9.6um'       = @{ Isotopologues = '16,17,18'; From = 950; To = 1200; File = 'hitran-o3-9.6um.csv' }
    'n2o-7.8um'      = @{ Isotopologues = '21';     From = 1200; To = 1350; File = 'hitran-n2o-7.8um.csv' }
    'ch4-7.7um'      = @{ Isotopologues = '32,33';  From = 1200; To = 1400; File = 'hitran-ch4-7.7um.csv' }
    'h2o-bending'    = @{ Isotopologues = '1,2,3';  From = 1300; To = 2000; File = 'hitran-h2o-bend.csv' }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

if ($Molecule -eq 'all') {
    foreach ($name in $bands.Keys) {
        & $PSCommandPath -Molecule $name -OutputDirectory $OutputDirectory
    }
    return
}

$band = $bands[$Molecule]
$destination = Join-Path $OutputDirectory $band.File

# nu = line centre, sw = intensity at 296 K, gamma_air = air-broadened half-width,
# n_air = its temperature exponent. The reader in Core expects exactly this order.
$parameters = 'nu,sw,gamma_air,n_air'
$uri = "https://hitran.org/lbl/api?iso_ids_list=$($band.Isotopologues)" +
       "&numin=$($band.From)&numax=$($band.To)" +
       "&head=False&fixwidth=0&sep=[comma]&request_params=$parameters"

Write-Host "Fetching $Molecule from hitran.org ($($band.From)-$($band.To) cm^-1)..."

try {
    Invoke-WebRequest -Uri $uri -TimeoutSec 300 -UseBasicParsing -OutFile $destination
}
catch {
    throw "Download failed: $($_.Exception.Message). hitran.org enforces a daily request limit; if this persists, try again later."
}

$file = Get-Item $destination
$count = (Get-Content $destination | Measure-Object -Line).Lines

if ($count -eq 0) {
    Remove-Item $destination -Force
    throw "hitran.org returned an empty list for $Molecule. Nothing was written."
}

Write-Host ""
Write-Host ("Wrote {0:N0} lines ({1:N1} KB) to {2}" -f $count, ($file.Length / 1KB), $destination) -ForegroundColor Green
Write-Host "The line-by-line tests will now use real data instead of skipping."
Write-Host ""
Write-Host "Cite HITRAN if you publish anything based on this:"
Write-Host "  Gordon et al., JQSRT 277, 107949 (2022)."
