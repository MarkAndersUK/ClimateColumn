<#
.SYNOPSIS
  Prints docs/model-description.html to PDF and records the source hash it was printed from.

.DESCRIPTION
  The PDF is committed alongside its HTML source because it is the form most people actually
  read. That creates a staleness problem: edit the HTML, forget to re-print, and the repository
  ships a document that disagrees with itself.

  So this does both halves together - print the PDF, and write the SHA-256 of the HTML it came
  from into docs/model-description.sha256. CI recomputes that hash and fails if it has moved,
  which catches the forgotten re-print. Doing them in one script is what makes the pairing hard
  to break: there is no way to print without recording, or to record without printing.

  A note on why CI checks a hash rather than the PDF's own text. The obvious check is to
  re-print in CI and compare - but a runner does not have this document's fonts, and the
  results grid at the top linearises differently when its labels wrap differently. Extracted
  text came out as "SURFACE TEMPERATURE / OUTGOING LONGWAVE" on one and "SURFACE / OUTGOING /
  GREENHOUSE / TEMPERATURE / LONGWAVE / EFFECT" on the other: the same words, read down the
  columns instead of across them. That is a property of reconstructing reading order from
  glyph positions, and no amount of whitespace normalisation fixes it. Hashing the source
  answers the question actually being asked - was the PDF regenerated after the source last
  changed - and answers it exactly.
#>

[CmdletBinding()]
param(
    # Where to find a browser to print with. Chrome and Edge both work; Edge is present on
    # every Windows install, so it is the fallback rather than a requirement.
    [string] $Browser
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$html = Join-Path $root 'docs\model-description.html'
$pdf  = Join-Path $root 'docs\ClimateColumn-model-description.pdf'
$hash = Join-Path $root 'docs\model-description.sha256'

if (-not (Test-Path $html)) { throw "no source at $html" }

if (-not $Browser) {
    $candidates = @(
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe"
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )
    $Browser = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $Browser) { throw 'no Chrome or Edge found; pass -Browser <path to the exe>' }

Write-Host "printing with $Browser"

# --no-pdf-header-footer suppresses the browser's own page numbers and URL stamp; the document
# supplies its own footer through the print stylesheet.
#
# Chrome reports success on stderr ("N bytes written to file ..."), and Windows PowerShell wraps
# any stderr from a native command in an ErrorRecord - which, under ErrorActionPreference =
# Stop, turns a successful print into a thrown exception. So stderr is discarded and the exit
# code is checked instead, which is the thing that actually says whether it worked.
$ErrorActionPreference = 'Continue'
& $Browser --headless --disable-gpu --no-pdf-header-footer `
    "--print-to-pdf=$pdf" "file:///$($html -replace '\\', '/')" 2>$null | Out-Null
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($code -ne 0) { throw "the browser exited with $code" }
if (-not (Test-Path $pdf)) { throw 'the browser wrote no PDF' }

$size = (Get-Item $pdf).Length
if ($size -lt 50000) { throw "the PDF is only $size bytes, which is too small to be the document" }

# Hashed as bytes, not as text. .gitattributes pins every text file to LF in the repository and
# in the working tree, so these bytes are the same on Windows and on a Linux runner and the
# hash can be compared directly across them.
$sha = (Get-FileHash -Path $html -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $hash -Value $sha -Encoding ascii -NoNewline

Write-Host ("wrote {0} ({1:N0} bytes)" -f (Resolve-Path $pdf -Relative), $size)
Write-Host "source sha256 $sha"
