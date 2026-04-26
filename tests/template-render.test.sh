#!/usr/bin/env bash
# Golden-diff test harness for the Homebrew and Scoop render scripts.
# Used by both the local developer (manual run) and packaging.yml (CI).
#
# Exit 0 on all-pass, 1 on any diff / failure.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SUMS="$ROOT/tests/fixtures/SHA256SUMS.example.txt"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "-- homebrew render"
"$ROOT/tools/render-homebrew-formula.sh" 0.4.0 v0.4.0 "$SUMS" > "$TMP/brainyz.rb"
if command -v ruby >/dev/null 2>&1; then
  ruby -c "$TMP/brainyz.rb"
else
  echo "   ruby not installed; skipping syntax check"
fi
diff -u "$ROOT/tests/fixtures/homebrew-brainyz.rb.expected" "$TMP/brainyz.rb"

echo "-- scoop render"
"$ROOT/tools/render-scoop-manifest.sh" 0.4.0 v0.4.0 "$SUMS" > "$TMP/brainyz.json"
if command -v jq >/dev/null 2>&1; then
  jq empty "$TMP/brainyz.json"
elif command -v python >/dev/null 2>&1; then
  python -c "import json,sys; json.load(open(sys.argv[1]))" "$TMP/brainyz.json"
elif command -v python3 >/dev/null 2>&1; then
  python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$TMP/brainyz.json"
else
  echo "   neither jq nor python installed; skipping JSON syntax check"
fi
diff -u "$ROOT/tests/fixtures/scoop-brainyz.json.expected" "$TMP/brainyz.json"

echo "-- missing-checksum failure mode"
echo "0000000000000000000000000000000000000000000000000000000000000001  brainz-linux-x64.tar.gz" > "$TMP/partial.txt"
if "$ROOT/tools/render-homebrew-formula.sh" 0.4.0 v0.4.0 "$TMP/partial.txt" >/dev/null 2>"$TMP/err"; then
  echo "FAIL: render should have errored on missing checksums"
  exit 1
fi
grep -q "missing checksum for brainz-osx-arm64.tar.gz" "$TMP/err" || {
  echo "FAIL: expected error message not found"
  cat "$TMP/err"
  exit 1
}

echo "ALL GREEN"
