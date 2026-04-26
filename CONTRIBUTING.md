# Contributing to brainyz

brainyz is a personal open-source project built in the open. Contributions are
welcome, but the bar is set deliberately high for changes that affect the
architecture or the user-facing surface.

## Getting started

```bash
git clone https://github.com/FavitoX/brainyz
cd brainyz
dotnet test              # 57 tests, should be green
```

Requires: **.NET 10 SDK**. For NativeAOT publishing locally you also need a
platform C/C++ toolchain (Visual Studio Build Tools with the C++ workload on
Windows; `clang` + `zlib1g-dev` on Linux; Xcode Command Line Tools on macOS).

## What lands quickly

- Bug fixes with a failing test that demonstrates the bug.
- Documentation corrections and examples.
- Dependency bumps that pass CI.
- Small, focused improvements to existing commands / MCP tools.

## What requires an issue first

- Anything that changes the schema (`schema.sql`).
- New subcommands or MCP tools.
- Changes to the scope resolution chain (D-010) or the embedding pipeline.
- New dependencies.

Please open an issue describing the motivation, alternatives considered, and
the proposed change before writing a large PR — every accepted change becomes
a long-term maintenance commitment and we'd rather align early.

## Coding conventions

- Standard C# / .NET idioms: `PascalCase` for types and members,
  `camelCase` for locals and parameters, `_camelCase` for private fields.
- `snake_case` only where the outer ecosystem expects it: SQL column names
  (already set in `schema.sql`), TOML config keys, JSON-RPC method names,
  and CLI long flags.
- Every `.cs` file must start with the two-line SPDX header:
  ```csharp
  // Copyright 2026 Favio Andres Leyva
  // SPDX-License-Identifier: Apache-2.0
  ```
- Comments, XML doc comments, and commit messages are in **English**. The
  rationale is OSS reach; the author is native Spanish speaker but the audience
  is international.
- Error messages that surface to end users can be bilingual when the author's
  immediate audience is the project maintainer (CLI warnings, etc.).

## Tests

All changes that touch `Brainyz.Core` or `Brainyz.Cli` need tests. Tests run
against a real LibSQL database in a temporary directory (no mocking of the
store). See `tests/Brainyz.Tests/StoreTestBase.cs` for the fixture.

Embedding tests use synthetic `float[]` vectors so they don't require Ollama.

```bash
dotnet test
```

## Commits

- English, imperative ("add scope resolver" not "added scope resolver").
- One logical change per commit. No trailing "WIP", "tests", "fixup" in merged
  history.

## Releasing

Stable releases are cut by pushing a `vX.Y.Z` tag (no pre-release
suffix). The `.github/workflows/release.yml` workflow then:

1. Builds + smoke-tests the 4-RID NativeAOT matrix.
2. Publishes the GitHub Release with archives + `SHA256SUMS.txt`.
3. Pushes an updated formula to `chaosfabric/homebrew-tap` and an
   updated manifest to `chaosfabric/scoop-bucket`.

Winget and AUR are bootstrapped manually on each release for v0.4 —
see [docs/packaging.md](docs/packaging.md) for the step-by-step.

Pre-release tags (anything containing `-`, e.g. `v0.5.0-rc.1`) skip
the package-manager updater jobs; only the GitHub Release is cut.

**After every stable release, before announcing**: run the
[per-release verification checklist](docs/packaging.md#per-release-verification-checklist)
in `docs/packaging.md`. The five-channel `brainz --version` smoke takes
~10 minutes against fresh containers and catches updater bugs before
users hit them.

## Licence

By contributing you agree that your contributions will be licensed under the
Apache License 2.0 (see `LICENSE`).
