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
    [ValidateSet('co2-15um', 'h2o-rotational')]
    [string] $Molecule = 'co2-15um',

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'data' }

# Global isotopologue ids as used by hitran.org: 7 = 12C16O2, 8 = 13C16O2, 1 = H2(16)O.
$bands = @{
    'co2-15um'       = @{ Isotopologues = '7,8'; From = 580; To = 760; File = 'hitran-co2-15um.csv' }
    'h2o-rotational' = @{ Isotopologues = '1';   From = 100; To = 500; File = 'hitran-h2o-rot.csv' }
}

$band = $bands[$Molecule]
$destination = Join-Path $OutputDirectory $band.File

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

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
