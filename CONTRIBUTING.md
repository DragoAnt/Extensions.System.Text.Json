# Contributing

Issues and pull requests are welcome. For anything larger than a small fix, open an issue first so the approach can be agreed before you write the code.

## Build and test

The shared MSBuild settings come from [DragoAnt.MSBuildKit](https://github.com/DragoAnt/MSBuildKit), committed under `.toolkit/`, so a plain clone builds:

```sh
git clone https://github.com/DragoAnt/Extensions.System.Text.Json.git
cd Extensions.System.Text.Json
```

You need the .NET SDK pinned in [global.json](./global.json), plus the .NET 8 runtime so the tests run for every target framework. Then run the same steps as CI:

```sh
dotnet restore
dotnet build -c Release --no-restore
dotnet test --solution DragoAnt.System.Text.Json.slnx -c Release --no-build
```

Until DragoAnt.Observer.Core and DragoAnt.Observer.Conformance 1.0.0-beta.2 are on nuget.org, clone [DragoAnt/Observer](https://github.com/DragoAnt/Observer) next to this repository and add `-p:DragoAntObserverLocal=true` to each command (or `-p:DragoAntObserverRoot=<path>/` for another location).

CI runs these steps on Ubuntu and Windows.

To run the same tests against a version published on nuget.org, use the [package pilot](./.pilot/README.md).

The stack:

- [xUnit v3](https://xunit.net/docs/getting-started/v3/cmdline) on [Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro) v2.
- [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions) for assertions.
- [Verify](https://github.com/VerifyTests/Verify) snapshots, pinned to [32.0.0](https://www.nuget.org/packages/Verify.XunitV3/32.0.0): later versions are expected to need a paid maintenance subscription, while 32.0.0 is MIT. When a snapshot changes on purpose, review the `*.received.*` file and replace the matching `*.verified.*` file with it.
- [package validation](https://learn.microsoft.com/dotnet/fundamentals/apicompat/package-validation/overview) on every pack, against the last published release.
- [BenchmarkDotNet](https://benchmarkdotnet.org/) for performance claims (see Benchmarks below).

Core tests live in `DragoAnt.System.Text.Json.Observer.Tests.Shared` as abstract classes, each run by a sealed `Run…` class in `DragoAnt.System.Text.Json.Observer.Tests` for every target framework.

### Code in the READMEs is tested

`DragoAnt.System.Text.Json.Observer.Docs.Tests` compiles every ```` ```csharp ```` block of [README.md](./README.md) and of the package README against the current library, runs it, and compares what it prints with its `// Output:` comment. Write each block as a complete program (usings, top-level statements, then any types). Put `<!-- doc-test: skip -->` on the line before a block that is only a fragment.

### Coverage

```sh
dotnet test --solution DragoAnt.System.Text.Json.slnx -c Release --no-build --coverage --coverage-output-format cobertura --results-directory TestResults
pwsh tools/check-coverage.ps1 -ResultsDirectory TestResults -Package DragoAnt.System.Text.Json.Observer -MinLine 92 -MinBranch 80
```

The script merges the reports of every test run and fails below the thresholds; keep the core library at **92 % line / 80 % branch** or above (95 % / 82 % today). The `ci` workflow also gates on overall coverage.

### Public API

The core library builds with `EnforcePublicApiDocs`: a public member without an XML doc comment fails the build. Write the comment for callers — what the member does and what they observe — not how it works inside.

### Benchmarks

Benchmarks live in `DragoAnt.System.Text.Json.Observer.Benchmarks` ([BenchmarkDotNet](https://benchmarkdotnet.org/)):

```sh
dotnet run -c Release --project DragoAnt.System.Text.Json.Observer.Benchmarks
```

CI runs the allocation gate on `net10.0`: it fails when a gated call allocates more than its `[AllocationBudget]` plus 23 B of meter noise, when the control case allocates less than its lower limit, or when a benchmark has no budget. Run it locally with:

```sh
dotnet run -c Release -f net10.0 --project DragoAnt.System.Text.Json.Observer.Benchmarks -- --filter '*AllocationGate*'
```

### Build kit

`.toolkit/` is installed by DragoAnt.MSBuildKit; don't edit it by hand. Move to another kit release with its update script and commit the result:

```sh
sh .toolkit/update.sh --version 0.2.0      # or: pwsh .toolkit/update.ps1 -Version 0.2.0
```

Repository settings live in `Directory.Build.props` (target frameworks, copyright), `Directory.Version.props` (the next release's `VersionPrefix`) and `Directory.Packages.props` (package versions the kit does not provide).

## Pull requests

- Branch from `main` and target `main`.
- Add or update tests for every behavior change; add a regression test for a bug fix.
- Keep the build warning-free: warnings are treated as errors.
- Update [README.md](./README.md) and the package README (`DragoAnt.System.Text.Json.Observer/package.readme.md`) when usage changes.
- The `ci` workflow must pass on the pull request.

Releases are published to nuget.org by the maintainers from a GitHub release.

## Security

Do not open a public issue for a vulnerability — see [SECURITY.md](./SECURITY.md).
