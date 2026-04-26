# Changelog

All notable changes to brainyz are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.0] — 2026-04-26

### Added
- Homebrew tap — `brew install chaosfabric/tap/brainyz` (macOS arm64,
  Linux x64, Linux arm64). Installs both `brainz` and `bz` on `PATH`.
- Scoop bucket — `scoop bucket add chaosfabric
  https://github.com/chaosfabric/scoop-bucket && scoop install
  brainyz` (Windows x64).
- Winget — `winget install ChaosFabric.Brainyz` (Windows x64,
  bootstrapped manually in this release; auto-update from v0.5+).
- AUR — `yay -S brainyz-bin` (Arch Linux x64, bootstrapped manually
  in this release; auto-update from v0.5+).
- `SHA256SUMS.txt` published with every release as the trust root
  for every downstream package manifest.
- `brainz --version` now prints `brainyz <semver>` (was the raw
  assembly version). Package-manager lint steps assert on this format.
- `docs/packaging.md` — channels overview, release-time automation,
  manual bootstrap, per-release verification checklist, rollback.
- Weekly `packaging.yml` workflow: golden-diff tests on the render
  scripts, `brew audit --strict` on the tap formula, and drift
  detection across all four channels (opens/updates a GitHub issue if
  any channel falls behind).

### Changed
- `Brainyz.Cli.csproj` derives `<Version>` from `BrainyzCliVersion.Current`
  via an MSBuild `GetVersionFromSource` target, keeping a single source
  of truth for the CLI version string.
- `release.yml` gains two updater jobs (`update-homebrew-tap`,
  `update-scoop-bucket`) that render templated manifests from the
  release's `SHA256SUMS.txt` and push to the `chaosfabric/*` repos via
  deploy keys. Both skip on pre-release tags and are
  `continue-on-error: true` so a packaging hiccup can't block a
  release.

### Internal
- `tools/templates/` — Homebrew formula + Scoop manifest templates.
- `tools/render-homebrew-formula.sh`, `tools/render-scoop-manifest.sh` —
  bash + awk + sed renderers.
- `tests/fixtures/SHA256SUMS.example.txt` + golden expected outputs;
  `tests/template-render.test.sh` harness.
- `tools/winget-manifests/` + `tools/aur/PKGBUILD` — templates checked
  in for reference during the manual bootstrap of each new release.

## [0.3.0] — 2026-04-23

### Added
- `brainz export --to <path.jsonl>` — streaming JSONL export of the entire
  brain. Record types cover projects, remotes, decisions, alternatives,
  principles, notes, tags, tag links, cross-entity links, and embeddings
  (base64-encoded F32 blobs). `--project <id>` filters the export to a
  single scope; `--include-history` adds the audit-log snapshots.
- `brainz import --from <path.jsonl>` — ingest a JSONL export into the
  local brain. `--mode replace` (default) wipes the tables first; `--mode
  additive` inserts only rows whose ULID is not yet present. `--dry-run`
  validates the file without mutating the DB. An automatic
  `brainyz.pre-import-<timestamp>.db` safety snapshot is written next to
  the DB unless `--no-backup` is passed.
- `brainz backup --to <path.zip>` — atomic snapshot of the DB using
  `VACUUM INTO`, packaged in a ZIP next to a `manifest.json` and a
  human-readable `README.txt`. `--compression fastest|optimal|none`
  (default `fastest`).
- `brainz restore --from <path.zip>` — restore a backup zip. Validates
  the manifest (format version, schema version), writes a pre-restore
  safety backup, and atomically swaps the extracted DB into place.
  Interactive TTY stdin prompts for confirmation; piped/redirected stdin
  behaves like `--force` (§5.5 of the spec). Detects other processes
  holding the target DB with a retrying File.Move and reports them via
  `BZ_RESTORE_DB_LOCKED`.
- MCP tools: `ExportBrain` and `BackupBrain` — both read-only from the
  DB's perspective and safe for agents to trigger. `import` and
  `restore` are **not** exposed over MCP (destructive).
- Unified error-code system: every brainyz command now surfaces typed
  `BZ_<CATEGORY>_<NAME>` codes with an optional tip and a
  `docs/errors.md` anchor. Exit codes follow a small convention —
  `1` operational failure, `2` validation failure, `3` user declined an
  interactive prompt.
- Release smoke-test (`tools/smoke-test.cs`) extended with steps 10
  (`backup`) and 11 (`restore`) that run against the published AOT
  binary on each RID in `release.yml`.

### Changed
- `BrainStore` exposes `DbPath` (public) and `Connection` (internal) for
  the export/import/backup/restore paths. Callers still use the typed
  per-entity helpers; raw SQL access is deliberately scoped.
- `Brainyz.Core.Storage.BrainStore.VectorLiteral` is now `internal` so
  the importer can re-use the exact `vector32()` literal form existing
  Upsert paths produce.
- `Brainyz.Core.csproj` and `Brainyz.Cli.csproj` grant
  `InternalsVisibleTo("Brainyz.Tests")` — the new export/import tests
  need reach into Core's SQL helpers and the CLI's `ShouldPrompt` decision
  helper. No `public` API surface changed for runtime consumers.
- `.gitignore` whitelist entries for `src/Brainyz.Core/Backup/` and
  `tests/Brainyz.Tests/Backup/` — the repo-wide `Backup*/` VS-artifact
  pattern was swallowing legit source.

### Documentation
- New `docs/errors.md` — every `BZ_*` code documented with meaning,
  common causes, and recovery steps. CLI error output links here.
- New `docs/superpowers/specs/2026-04-23-v0.3-export-import-backup-design.md`
  and `docs/superpowers/plans/2026-04-23-v0.3-export-import-backup.md`
  — the design spec and the bite-sized implementation plan used to ship
  this release.

### Internal
- Zero new runtime dependencies. Everything builds on the BCL:
  `System.IO.Compression.ZipArchive`, `System.Text.Json` with source
  generators (via new `BrainyzJsonlContext`), `VACUUM INTO`, and
  base64/SHA256 from `System.Security.Cryptography`.
- Shared `Brainyz.Core.Safety.SafetyBackup` helper covers both the
  pre-import and pre-restore safety snapshots.

## [0.2.0] — 2026-04-22

### Added
- CRUD completeness: `brainz edit` and `brainz delete` commands with
  confirmation prompt (skippable via `--force` or non-TTY stdin), plus
  matching `update_*` and `delete_*` MCP tools.
- E2E smoke-test script (`tools/smoke-test.cs`) wired into the release
  workflow so every published AOT binary is exercised before the GitHub
  Release is cut.
- CI non-blocking smoke on `linux-arm64`: the archive ships without a
  native `libsql.so` (Nelknet.LibSQL does not bundle one for that RID);
  the smoke failure is logged but does not block the release.

### Changed
- Nelknet.LibSQL.Data 0.2.5 → 0.2.6 (pure native engine bump; libSQL
  upstream `libsql-server-v0.24.32`).
- NativeAOT workaround refined for single-file publishes: the
  reflection hack that flips Nelknet's `_isInitialized` is now
  AOT-trim-safe via `[DynamicDependency]`, and Nelknet's own
  initialization is short-circuited so it skips its broken
  `Assembly.Location`-based search.

### Fixed
- Single-file AOT "Failed to load libSQL native library" crash on
  every platform (previously shipped broken in v0.1.0-rc.\*).

## [0.1.0] — 2026-04-21

Initial public release. CLI + MCP server + hybrid search + local
embeddings via Ollama + per-project scoping. See
[brainyz-decisions.md](brainyz-decisions.md) for the full design
trail.

[Unreleased]: https://github.com/FavitoX/brainyz/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/FavitoX/brainyz/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/FavitoX/brainyz/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/FavitoX/brainyz/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/FavitoX/brainyz/releases/tag/v0.1.0
