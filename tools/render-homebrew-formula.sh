#!/usr/bin/env bash
# Render tools/templates/homebrew-brainyz.rb.tmpl for a given release.
#
# Usage: render-homebrew-formula.sh <version> <tag> <sha256sums-path>
#   version          — semver without 'v', e.g. "0.4.0"
#   tag              — tag name with 'v', e.g. "v0.4.0"
#   sha256sums-path  — path to a SHA256SUMS.txt as emitted by `sha256sum`
#
# Stdout: rendered Ruby formula.
# Stderr: human-readable errors on any missing/malformed input.
# Exit:   0 on success, 1 on any error.

set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <version> <tag> <sha256sums-path>" >&2
  exit 1
fi

VERSION="$1"
TAG="$2"
SUMS="$3"

if [[ ! -f "$SUMS" ]]; then
  echo "error: SHA256SUMS file not found: $SUMS" >&2
  exit 1
fi

sha_for() {
  local file="$1"
  local sha
  sha=$(awk -v f="$file" '$2 == f { print $1; found=1 } END { exit !found }' "$SUMS") || {
    echo "error: missing checksum for $file in $SUMS" >&2
    return 1
  }
  printf '%s' "$sha"
}

SHA_OSX_ARM64=$(sha_for   "brainz-osx-arm64.tar.gz")
SHA_LINUX_X64=$(sha_for   "brainz-linux-x64.tar.gz")
SHA_LINUX_ARM64=$(sha_for "brainz-linux-arm64.tar.gz")

TMPL_DIR="$(cd "$(dirname "$0")/templates" && pwd)"
sed -e "s/{{VERSION}}/$VERSION/g" \
    -e "s/{{TAG}}/$TAG/g" \
    -e "s/{{SHA256_OSX_ARM64}}/$SHA_OSX_ARM64/g" \
    -e "s/{{SHA256_LINUX_X64}}/$SHA_LINUX_X64/g" \
    -e "s/{{SHA256_LINUX_ARM64}}/$SHA_LINUX_ARM64/g" \
    "$TMPL_DIR/homebrew-brainyz.rb.tmpl"
