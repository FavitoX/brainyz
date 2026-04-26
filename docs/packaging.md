# Packaging

brainyz ships through four native package managers in addition to
[GitHub Releases](https://github.com/FavitoX/brainyz/releases). This
doc is the reference for how each channel works, how to bootstrap a new
release, and how to roll back a bad one.

## Channels

| Channel | Repository | Install command |
|---|---|---|
| Homebrew tap | [`chaosfabric/homebrew-tap`](https://github.com/chaosfabric/homebrew-tap) | `brew install chaosfabric/tap/brainyz` |
| Scoop bucket | [`chaosfabric/scoop-bucket`](https://github.com/chaosfabric/scoop-bucket) | `scoop bucket add chaosfabric https://github.com/chaosfabric/scoop-bucket; scoop install brainyz` |
| Winget | [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) (mirror at [`chaosfabric/winget-manifests`](https://github.com/chaosfabric/winget-manifests)) | `winget install ChaosFabric.Brainyz` |
| AUR | [`aur/brainyz-bin`](https://aur.archlinux.org/packages/brainyz-bin) (mirror at [`chaosfabric/aur-brainyz-bin`](https://github.com/chaosfabric/aur-brainyz-bin)) | `yay -S brainyz-bin` |

`SHA256SUMS.txt` published with each GitHub Release is the trust root
for all four channels; manifests pin to it directly.

## Release-time automation

On every stable tag (`vX.Y.Z` with no pre-release suffix), `.github/workflows/release.yml`:

1. Builds `brainz` for 4 RIDs (linux-x64, linux-arm64, osx-arm64, win-x64).
2. Runs the smoke-test per RID.
3. Generates `SHA256SUMS.txt` (`checksums` job).
4. Publishes the GitHub Release with archives + checksums.
5. **`update-homebrew-tap`** job: renders `Formula/brainyz.rb` from
   `tools/templates/homebrew-brainyz.rb.tmpl` + `tools/render-homebrew-formula.sh`,
   pushes to `chaosfabric/homebrew-tap`.
6. **`update-scoop-bucket`** job: renders `bucket/brainyz.json` similarly,
   pushes to `chaosfabric/scoop-bucket`.

Both updater jobs are `continue-on-error: true` and skip on
pre-release tags (anything with `-`). The drift-detection job in
`packaging.yml` opens an issue if any channel falls behind.

Winget and AUR are **not automated in v0.4**; they are bootstrapped
manually per the next section. v0.5+ will add `update-winget` and
`update-aur` jobs once the first-release manifests are proven.

## Manual bootstrap (first release per channel)

### Winget (`ChaosFabric.Brainyz`)

**Prereqs**:
- Windows box.
- `winget install Microsoft.WingetCreate`.
- GitHub Personal Access Token with `public_repo` scope
  (https://github.com/settings/tokens — Generate new (classic)).

**Steps** (run after the GitHub Release for the new version is published):

```powershell
# 1. Copy template files into a working directory:
$VER = "0.4.0"
mkdir winget-bootstrap\$VER
Copy-Item tools\winget-manifests\*.yaml winget-bootstrap\$VER\

# 2. Replace {{VERSION}} and {{SHA256_WIN_X64}} placeholders.
#    Get the SHA256 from the release's SHA256SUMS.txt for brainz-win-x64.zip.
$sha = (Invoke-WebRequest "https://github.com/FavitoX/brainyz/releases/download/v$VER/SHA256SUMS.txt").Content `
       -split "`n" | Where-Object { $_ -like "*brainz-win-x64.zip" } `
       | ForEach-Object { ($_ -split "\s+")[0] }

Get-ChildItem winget-bootstrap\$VER\*.yaml | ForEach-Object {
  (Get-Content $_ -Raw) -replace '\{\{VERSION\}\}', $VER -replace '\{\{SHA256_WIN_X64\}\}', $sha `
    | Set-Content $_
}

# 3. Validate locally:
winget validate winget-bootstrap\$VER\

# 4. Snapshot to shadow repo chaosfabric/winget-manifests:
#    (clone the shadow repo, copy the 3 YAMLs under
#     manifests/c/ChaosFabric/Brainyz/$VER/, commit + push)

# 5. Submit the PR to microsoft/winget-pkgs:
wingetcreate submit --token $env:WINGET_PAT winget-bootstrap\$VER\
```

Microsoft's CI validates in ~5–20 minutes; human review 2–7 days.
Address any lint failures and push new commits to the same PR.

**If the validator rejects the dual-`PortableCommandAlias` entries**:
the `bz.cmd` fallback is documented in the design spec §11 but not
wired; surface to Favio for a design pivot before forcing a
workaround.

### AUR (`brainyz-bin`)

**Prereqs**:
- Arch Linux environment (WSL Arch, Docker `archlinux:latest`, or VM).
- `pacman -S --needed base-devel git namcap`.
- SSH key registered at https://aur.archlinux.org → My Account.
  `~/.ssh/config`:
  ```
  Host aur.archlinux.org
    IdentityFile ~/.ssh/aur-deploy
    User aur
  ```

**Steps** (run after the GitHub Release):

```bash
VER=0.4.0
# 1. Clone the AUR repo (empty on first push):
git clone ssh://aur@aur.archlinux.org/brainyz-bin.git
cd brainyz-bin

# 2. Copy the template and fill in the real SHA256:
cp ../../brainyz/tools/aur/PKGBUILD .
SHA=$(curl -fsSL "https://github.com/FavitoX/brainyz/releases/download/v$VER/SHA256SUMS.txt" \
      | awk '$2 == "brainz-linux-x64.tar.gz" { print $1 }')
sed -i "s|sha256sums_x86_64=('SKIP')|sha256sums_x86_64=('$SHA')|" PKGBUILD

# 3. Generate .SRCINFO (AUR requires it committed):
makepkg --printsrcinfo > .SRCINFO

# 4. Lint:
namcap PKGBUILD

# 5. Build + install locally to smoke-test:
makepkg -si
brainz --version      # expected: 'brainyz 0.4.0'
LD_DEBUG=libs brainz --version 2>&1 | grep libsql
# ^-- must show libsql.so being loaded from /usr/lib/brainyz/ (via $ORIGIN).

# 6. Push to AUR:
git add PKGBUILD .SRCINFO
git commit -m "v$VER"
git push

# 7. Snapshot to shadow repo chaosfabric/aur-brainyz-bin.
```

**If `LD_DEBUG=libs` shows `libsql.so` not found via `$ORIGIN`**: drop
in the `LD_LIBRARY_PATH` wrapper from the design spec §4.4 fallback
block. File an issue noting the Arch glibc version.

## Per-release verification checklist

Run on clean machines (fresh container / VM / WSL distro) after each
stable release:

- [ ] **Homebrew (macOS arm64)**: `brew install chaosfabric/tap/brainyz`; then `brainz --version` and `bz --version` both print `brainyz <ver>`.
- [ ] **Homebrew (Linux x64)**: same, in a fresh Ubuntu container with Homebrew on Linux.
- [ ] **Scoop (Windows x64)**: `scoop install chaosfabric/brainyz`; `brainz --version` + `bz --version`.
- [ ] **Winget (Windows x64)**: `winget install ChaosFabric.Brainyz`; `brainz --version` + `bz --version`.
- [ ] **AUR (Arch x64)**: `yay -S brainyz-bin`; `brainz --version` + `bz --version` + `LD_DEBUG=libs brainz --version 2>&1 | grep libsql` finds the lib.
- [ ] Each channel: a minimal smoke cycle — `brainz init --here` in a tempdir → `brainz add note "smoke"` → `brainz list` returns a row.

## Rollback

If a release ships broken manifests to any channel:

| Channel | Action |
|---|---|
| Homebrew tap | `git revert <bad-commit> && git push` on `chaosfabric/homebrew-tap`. |
| Scoop bucket | `git revert <bad-commit> && git push` on `chaosfabric/scoop-bucket`. |
| Winget | PR to `microsoft/winget-pkgs` removing the offending version folder. |
| AUR | `git push` previous PKGBUILD (`git revert` + `git push` to the AUR remote). |

The GitHub Release itself is never edited — the underlying archives
remain available for manual install throughout. Users already on the
bad version downgrade with the package manager's standard
uninstall+install at the prior version.

## Future automation (v0.5+)

- **Winget auto-update**: CI job running `wingetcreate update --urls <new-url> --version <ver> ChaosFabric.Brainyz --submit --token $WINGET_PAT` on each stable tag. Needs a `WINGET_PAT` repo secret.
- **AUR auto-push**: CI job templating PKGBUILD + `.SRCINFO`, `git push`-ing to `ssh://aur@aur.archlinux.org/brainyz-bin.git` over an `AUR_SSH_KEY` secret.
- **Homebrew core submission**: after six consecutive stable releases with no regressions AND project meets
  [Acceptable Formulae](https://docs.brew.sh/Acceptable-Formulae) criteria.
- **nix flake**: when requested via issue.

## See also

- Design spec: [docs/superpowers/specs/2026-04-24-v0.4-packaging-design.md](superpowers/specs/2026-04-24-v0.4-packaging-design.md)
- Implementation plan: [docs/superpowers/plans/2026-04-24-v0.4-packaging.md](superpowers/plans/2026-04-24-v0.4-packaging.md)
- Error codes: [docs/errors.md](errors.md)
- Nelknet upstream gaps affecting packaging: [nelknet-bugs.md](../nelknet-bugs.md) (particularly §8, linux-arm64 native).
