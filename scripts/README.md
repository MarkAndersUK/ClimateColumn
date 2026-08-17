# scripts

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
