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
dotnet test --solution DragoAnt.System.Text.Json.sln -c Release --no-build
```

Tests use xUnit v3 on Microsoft.Testing.Platform, with Verify snapshots. When a snapshot changes on purpose, review the `*.received.*` file and replace the matching `*.verified.*` file with it.

Benchmarks live in `DragoAnt.System.Text.Json.Observer.Benchmarks` (BenchmarkDotNet):

```sh
dotnet run -c Release --project DragoAnt.System.Text.Json.Observer.Benchmarks
```

## Pull requests

- Branch from `main` and target `main`.
- Add or update tests for every behavior change; add a regression test for a bug fix.
- Keep the build warning-free: warnings are treated as errors.
- Update [README.md](./README.md) and the package README (`DragoAnt.System.Text.Json.Observer/package.readme.md`) when usage changes.
- The `build` workflow must pass on the pull request.

Releases are published to nuget.org by the maintainers from a GitHub release.

## Security

Do not open a public issue for a vulnerability — see [SECURITY.md](./SECURITY.md).
