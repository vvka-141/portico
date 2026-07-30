#!/usr/bin/env bash
# CHANGELOG.md shape gate.
#
# eng/extract-changelog-section.sh is tested against a fixture by
# eng/test-extract-changelog-section.sh. Nothing tested it against the real CHANGELOG.md, so the
# first time the actual file meets the actual extractor is at `git push --tags`.
#
# That is not catastrophic — release.yml extracts the notes BEFORE the nuget push, so a malformed
# section fails while the release is still reversible. It is merely expensive in the wrong place:
# the tag is already pushed, so recovering means deleting a tag and re-cutting it, and the person
# doing that is mid-release. Running the real extractor over the real file on every push moves the
# failure to the pull request, where fixing it costs a commit.
#
# What it does NOT check: whether the prose is accurate, or whether the entries match what actually
# changed. Those need a reader. This covers the mechanical part that the release depends on.

set -uo pipefail
cd "$(dirname "$0")/.."

changelog="CHANGELOG.md"
extract="eng/extract-changelog-section.sh"

failures=0
fail() { printf '\n[FAIL] %s\n' "$1"; failures=$((failures + 1)); }

if [[ ! -f "$changelog" ]]; then
  echo "error: $changelog not found" >&2
  exit 1
fi

# --- 1. Every version section is extractable and non-empty --------------------------------------
#
# The real extractor, not a reimplementation of it. A second parser here could agree with itself
# while disagreeing with the one the release actually runs, which is the whole failure being
# guarded against.

echo "Checking version sections are extractable..."

versions=$(grep -oE '^## \[[^]]+\]' "$changelog" | sed -E 's/^## \[(.*)\]$/\1/')

if [[ -z "$versions" ]]; then
  fail "$changelog has no '## [version]' headings at all. The release extractor keys on that shape."
fi

released=0
for version in $versions; do
  if ! bash "$extract" "$version" "$changelog" > /dev/null 2>&1; then
    fail "'## [$version]' cannot be extracted by $extract — the section is empty or malformed. This is what a release would hit."
  fi
  [[ "$version" == "Unreleased" ]] || released=$((released + 1))
done

# --- 2. The Unreleased heading exists ------------------------------------------------------------
#
# Keep a Changelog's convention, and the place the next release's notes accumulate. Without it
# there is nowhere to write an entry, so entries stop being written.

echo "Checking the Unreleased section..."

if ! grep -qE '^## \[Unreleased\]' "$changelog"; then
  fail "$changelog has no '## [Unreleased]' heading. That is where the next release's notes accumulate."
fi

# --- 3. Released sections carry a date -----------------------------------------------------------
#
# '## [0.1.1] - 2026-07-29'. A released version with no date reads as unreleased, and the date is
# the only thing in the file that says when a user's installed version was cut.

echo "Checking released sections carry a date..."

undated=$(grep -E '^## \[[^]]+\]' "$changelog" \
  | grep -vE '^## \[Unreleased\]' \
  | grep -vE '^## \[[^]]+\] - [0-9]{4}-[0-9]{2}-[0-9]{2}' || true)

if [[ -n "$undated" ]]; then
  fail "Released sections must carry an ISO date ('## [0.1.1] - 2026-07-29'):
$undated"
fi

if [[ "$released" -eq 0 ]]; then
  fail "$changelog documents no released version. If that is genuinely true, this gate is premature — delete it deliberately rather than letting it pass vacuously."
fi

# --- Result --------------------------------------------------------------------------------------

if [[ "$failures" -gt 0 ]]; then
  printf '\nCHANGELOG shape: %d problem(s)\n' "$failures"
  exit 1
fi

printf '\nCHANGELOG shape: OK (%d released section(s), all extractable)\n' "$released"
