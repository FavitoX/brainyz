#!/usr/bin/env bash
# Render tools/templates/scoop-brainyz.json.tmpl for a given release.
#
# Usage: render-scoop-manifest.sh <version> <tag> <sha256sums-path>
# See render-homebrew-formula.sh for the contract (identical shape).

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

SHA_WIN_X64=$(sha_for "brainz-win-x64.zip")

TMPL_DIR="$(cd "$(dirname "$0")/templates" && pwd)"
sed -e "s/{{VERSION}}/$VERSION/g" \
    -e "s/{{TAG}}/$TAG/g" \
    -e "s/{{SHA256_WIN_X64}}/$SHA_WIN_X64/g" \
    "$TMPL_DIR/scoop-brainyz.json.tmpl"
