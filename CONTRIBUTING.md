# Contributing

Issues and pull requests are welcome. For anything larger than a small fix, open an issue first so the approach can be agreed before you write the code.

## Build and test

The build imports shared MSBuild files from the `.msbuild` git submodule, so clone with submodules:

```sh
git clone --recurse-submodules https://github.com/DragoAnt/Extensions.System.Text.Json.git
cd Extensions.System.Text.Json
```

You need the .NET SDK pinned in [global.json](./global.json), plus the .NET 8 and .NET 9 runtimes so the tests run for every target framework. Then run the same steps as CI:

```sh
dotnet restore
dotnet build -c Release --no-restore
dotnet test --solution DragoAnt.System.Text.Json.slnx -c Release --no-build
```

Tests use xUnit v3 on Microsoft.Testing.Platform v2, with [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions) and Verify snapshots. When a snapshot changes on purpose, review the `*.received.*` file and replace the matching `*.verified.*` file with it. Core tests live in `DragoAnt.System.Text.Json.Observer.Tests.Shared` as abstract classes, each run by a sealed `Run…` class in `DragoAnt.System.Text.Json.Observer.Tests` for every target framework.

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

Benchmarks live in `DragoAnt.System.Text.Json.Observer.Benchmarks` (BenchmarkDotNet):

```sh
dotnet run -c Release --project DragoAnt.System.Text.Json.Observer.Benchmarks
```

## Pull requests

- Branch from `main` and target `main`.
- Add or update tests for every behavior change; add a regression test for a bug fix.
- Keep the build warning-free: warnings are treated as errors.
- Update [README.md](./README.md) and the package README (`DragoAnt.System.Text.Json.Observer/package.readme.md`) when usage changes.
- The `ci` workflow must pass on the pull request.

Releases are published to nuget.org by the maintainers from a GitHub release.

## Security

Do not open a public issue for a vulnerability — see [SECURITY.md](./SECURITY.md).
