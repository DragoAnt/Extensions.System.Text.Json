# Package pilot

The package pilot runs the repository's own tests against the packages **published on nuget.org** instead of the projects in this repository. A green source run proves the code; the pilot proves what users download.

## Why it exists

The source build cannot see these failures, because it never touches a `.nupkg`:

- a file that is not packed — a `buildTransitive` props file, a readme, an asset the library reads at run time;
- a wrong dependency or version range in the `.nuspec`, for example on [DragoAnt.Observer.Core](https://www.nuget.org/packages/DragoAnt.Observer.Core);
- a type or member that only exists when the library is built from source;
- a published binary built from a different commit, configuration or target framework than the one tested.

## What it runs

[`Pilot.slnx`](./Pilot.slnx) holds one project per test project of the repository:

| Pilot project | Runs the sources of | References the package |
| --- | --- | --- |
| `DragoAnt.System.Text.Json.Observer.Tests` | [`../DragoAnt.System.Text.Json.Observer.Tests`](../DragoAnt.System.Text.Json.Observer.Tests) and its `Tests.Shared` library | [DragoAnt.System.Text.Json.Observer](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer) |
| `DragoAnt.System.Text.Json.Observer.Http.Tests` | [`../DragoAnt.System.Text.Json.Observer.Http.Tests`](../DragoAnt.System.Text.Json.Observer.Http.Tests) | [DragoAnt.System.Text.Json.Observer.Http](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer.Http) |
| `DragoAnt.System.Text.Json.Observer.Http.IntegrationTests` | [`../DragoAnt.System.Text.Json.Observer.Http.IntegrationTests`](../DragoAnt.System.Text.Json.Observer.Http.IntegrationTests) and its host | DragoAnt.System.Text.Json.Observer.Http |
| `DragoAnt.System.Text.Json.Observer.Docs.Tests` | [`../DragoAnt.System.Text.Json.Observer.Docs.Tests`](../DragoAnt.System.Text.Json.Observer.Docs.Tests) | both |
| `DragoAnt.System.Text.Json.Observer.Skills.Tests` | [`../DragoAnt.System.Text.Json.Observer.Skills.Tests`](../DragoAnt.System.Text.Json.Observer.Skills.Tests) | both |

- **The sources are linked, never copied**: each pilot project compiles `..\..\<test project>\**\*.cs` with a wildcard, so a new test is in the pilot the moment it is in the repository. [Verify](https://github.com/VerifyTests/Verify) finds the `*.verified.*` snapshots next to the original sources, because the compiler records the original file paths.
- **Each pilot project keeps the original assembly name.** The published libraries carry `InternalsVisibleTo` for exactly those names and are not strong-named, so the tests that reach internals still compile against the package.
- **The package readme the doc tests read is the one inside the package** (`readme.md`, what nuget.org shows), not the repository file.
- The pilot is not in [`DragoAnt.System.Text.Json.slnx`](../DragoAnt.System.Text.Json.slnx): the normal build and CI never touch it.

## Run it

From the repository root (the [MTP](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro) runner is set in its `global.json`):

```sh
dotnet test --solution .pilot/Pilot.slnx -c Release                               # the version in Directory.Packages.props
dotnet test --solution .pilot/Pilot.slnx -c Release -p:PilotVersion=2.1.0         # another published version
```

`PilotVersion` is defined once, in [`Directory.Packages.props`](./Directory.Packages.props), and versions both packages; no pilot project names a version. Restore reads nuget.org only ([`nuget.config`](./nuget.config)). A package cache that already holds that version — from a local `dotnet pack`, say — would answer instead of nuget.org; to be sure, point the cache into the pilot (ignored by git):

```sh
NUGET_PACKAGES="$PWD/.pilot/packages" dotnet test --solution .pilot/Pilot.slnx -c Release
```

The local-override switches of the source build (`DragoAntObserverLocal`, `PackageAsProj_*`) cannot turn a package back into a project here: no pilot project references a library under test directly, and `obj/project.assets.json` lists every DragoAnt library as `"type": "package"` even with all of them set.

## Test an older version

The tests must match the version they test: newer tests fail against an older package for no fault of the package. Check out that release's tag in a worktree and run the pilot there:

```sh
git worktree add --detach ../stj-pilot-old v2.0.0-beta.1
cp -r .pilot ../stj-pilot-old/        # only when the tag is older than the pilot
cd ../stj-pilot-old
dotnet test --solution .pilot/Pilot.slnx -c Release -p:PilotVersion=2.0.0-beta.1
```

A copied pilot works while the test projects keep their names and folders; `DragoAnt.System.Text.Json.Observer.Http` exists from 2.0.0 only. Remove the worktree afterwards (`git worktree remove --force ../stj-pilot-old`).

## Reading the result

| Source run | Pilot | Meaning |
| --- | --- | --- |
| green | green | the published package behaves as the tested code |
| green | red | **the package differs from the source**: a file not packed, a dependency, a different build — look at the first failing test, not at the code |
| red | — | fix the code first; the pilot says nothing yet |

A test that can only pass when built from source is excluded by name in its pilot project and listed under known differences below, with the reason.

## Known differences

None: every test of the source run also runs, and passes, in the pilot.

## What it does not cover

- [Package validation](https://learn.microsoft.com/dotnet/fundamentals/apicompat/package-validation/overview) and the public API surface — the pack step on CI checks those.
- The agent skills: the packages ship no `skills/` folder, so `Skills.Tests` still reads them from the repository.
- `README.md` and `docs/` are repository files, not package content; the doc tests read them from the repository.
- The `.snupkg` symbols, Source Link, the signature, and any consumer on a framework the tests do not run (`net9.0`, `netstandard`).

## On CI

The pilot is not run by CI yet. After a release, a job could wait until the version is listed on nuget.org (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) and then run `dotnet test --solution .pilot/Pilot.slnx -c Release -p:PilotVersion=<tag without v>` on the release tag, on Ubuntu and Windows.
