# scripts

## fetch-hitran.ps1

Downloads a HITRAN line list so the band approximations can be checked against real spectral
data rather than only against synthetic lines.

```bash
pwsh scripts/fetch-hitran.ps1                              # CO2 15 um band
pwsh scripts/fetch-hitran.ps1 -Molecule h2o-rotational     # H2O rotational band
```

No API key is needed — hitran.org's line-by-line endpoint serves this anonymously; a key is only
required for the HAPI2 client library. There is a daily request limit, so don't loop it.

The tests skip per molecule, so fetching one, both, or neither all work.

The data is **not committed**. It is third-party data with its own citation requirement, and
leaving it out keeps the repository testable with no network and no external files: the tests
that want real lines skip when it is missing rather than failing. `data/` is already covered by
the `*.csv` ignore rule.

Cite HITRAN in published work: Gordon et al., *JQSRT* **277**, 107949 (2022).


## populate-package-cache.ps1

`nuget.config` clears every package source. That is deliberate: nothing in this repository
reaches the network at build time.

`ClimateColumn.Core` and `ClimateColumn.Cli` have no package dependencies at all, so they
build under that configuration from a completely cold cache. `ClimateColumn.Tests` is the
exception — it uses MSTest, and those packages have to reach the local NuGet cache
(`~/.nuget/packages`) once before an offline restore can find them.

This script does that one fetch. The source is passed on the command line, so the committed
`nuget.config` never gains a network source.

### Normal use, once per machine

```bash
pwsh scripts/populate-package-cache.ps1
```

Then everything works offline, indefinitely:

```bash
dotnet build ClimateColumn.sln -c Release
dotnet test ClimateColumn.sln -c Release
```

### Air-gapped machines

On a machine with network access, stage the packages:

```bash
pwsh scripts/populate-package-cache.ps1 -Export ../offline-nupkgs
```

That writes the resolved closure — 11 packages, 16.4 MB — as `.nupkg` files. The list is
read from the actual restore output rather than hard-coded, so it stays correct if the
package versions in the test project change.

Copy the folder across, then on the offline machine:

```bash
pwsh scripts/populate-package-cache.ps1 -Source /path/to/offline-nupkgs
```

### Why the payload is what it is

The test project runs on MSTest's own runner (Microsoft.Testing.Platform) rather than
VSTest, set by `EnableMSTestRunner` in `ClimateColumn.Tests.csproj`. That is what keeps the
staged payload small: adding `Microsoft.NET.Test.Sdk` back would pull in the VSTest host and
`Microsoft.CodeCoverage`, which this suite never uses, taking the closure from 11 packages
and 16.4 MB to 15 and 29.8 MB.

Both `dotnet test` and `dotnet run` work against the project, since the runner makes the
test assembly directly executable.

---

## `readme-to-html.js` and `build-readme-page.js`

Render this repository's README as a self-contained HTML page, for publishing somewhere that
does not render GitHub-flavoured Markdown or its maths.

```bash
node scripts/readme-to-html.js README.md /tmp/body.json
node scripts/build-readme-page.js /tmp/body.json /tmp/readme.html
```

These need Node — nothing else in the project does, and neither the build nor the test suite
depends on them. They exist because the README carries 208 inline LaTeX expressions and 17
display equations, and most viewers outside GitHub will show those as raw `$...$`.

**The maths is converted, not rendered by a library.** Superscripts and subscripts become
`<sup>`/`<sub>` rather than Unicode, because only a handful of characters have Unicode
superscript forms and everything else would silently degrade. A page that embeds KaTeX would
be an order of magnitude larger than the document, and a page that links to a CDN would break
under a strict content-security policy.

The first script reports any LaTeX command it does not recognise on stderr and exits non-zero,
rather than passing it through as literal text. That is the check worth keeping: three
conversion bugs were caught by it during development, and the two that were not — a subscript
losing its grouping, and fractions being destroyed by an over-eager brace strip — were found
only by reading all 17 equations one at a time afterwards. If either script is changed, do
that again.

Neither is a general Markdown or LaTeX implementation. Both handle exactly what this one
document contains.
