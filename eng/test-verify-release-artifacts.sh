#!/usr/bin/env bash
# Tests for eng/verify-release-artifacts.sh (POR-132).
#
# The script guards an irreversible step, so its own failure modes are worth pinning:
#   - The exact valid set (must succeed)
#   - A missing package (must fail, and name what was found instead)
#   - A package at another version (must fail)
#   - An unexpected package at the tagged version (must fail — the publish wildcard would ship it)
#   - Symbol packages: expected ones ignored, unexpected ones rejected
#   - A missing artifacts directory (must fail loudly)

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
verify="$script_dir/verify-release-artifacts.sh"

version="0.1.0"
pass=0
fail=0

workdir=$(mktemp -d)
trap 'rm -rf "$workdir"' EXIT

# A fresh artifacts directory holding the exact set a good release produces, plus whatever extra
# filenames the caller names. Each test gets its own so nothing leaks between them.
make_artifacts() {
  local dir="$workdir/$1"
  shift
  mkdir -p "$dir"
  local name
  for name in \
    "Portico.$version.nupkg" \
    "Portico.DependencyInjection.$version.nupkg" \
    "Portico.Hosting.$version.nupkg" \
    "Portico.Templates.$version.nupkg"; do
    echo "not really a zip" > "$dir/$name"
  done
  for name in "$@"; do
    echo "not really a zip" > "$dir/$name"
  done
  echo "$dir"
}

assert_exit() {
  local label="$1" expected_exit="$2" dir="$3"
  local actual_exit=0
  bash "$verify" "$version" "$dir" > /dev/null 2>&1 || actual_exit=$?
  if [[ "$actual_exit" -eq "$expected_exit" ]]; then
    echo "  PASS  $label"
    pass=$((pass + 1))
  else
    echo "  FAIL  $label (expected exit $expected_exit, got $actual_exit)"
    fail=$((fail + 1))
  fi
}

assert_output_contains() {
  local label="$1" needle="$2" dir="$3"
  local output
  output=$(bash "$verify" "$version" "$dir" 2>&1) || true
  if echo "$output" | grep -qF "$needle"; then
    echo "  PASS  $label"
    pass=$((pass + 1))
  else
    echo "  FAIL  $label (output did not contain '$needle')"
    echo "        got: $output"
    fail=$((fail + 1))
  fi
}

echo "verify-release-artifacts.sh"

# --- The valid set ---
valid=$(make_artifacts valid)
assert_exit "accepts the exact valid set" 0 "$valid"
assert_output_contains "reports the count and the exactness" "and nothing else" "$valid"

# --- Symbol packages ride along and are not a defect ---
symbols=$(make_artifacts symbols \
  "Portico.$version.snupkg" \
  "Portico.DependencyInjection.$version.snupkg" \
  "Portico.Hosting.$version.snupkg")
assert_exit "accepts the symbol packages that pack produces" 0 "$symbols"

# --- Missing ---
missing=$(make_artifacts missing)
rm "$missing/Portico.Hosting.$version.nupkg"
assert_exit "rejects a missing package" 1 "$missing"
assert_output_contains "names the missing package" "MISS  expected Portico.Hosting.$version.nupkg" "$missing"
assert_output_contains "says nothing was built when nothing was" "no Portico.Hosting package was built at all" "$missing"

# --- Wrong version ---
wrong=$(make_artifacts wrong)
rm "$wrong/Portico.Hosting.$version.nupkg"
echo "not really a zip" > "$wrong/Portico.Hosting.0.2.0.nupkg"
assert_exit "rejects a package at another version" 1 "$wrong"
assert_output_contains "names what was found instead" "found instead: $wrong/Portico.Hosting.0.2.0.nupkg" "$wrong"
assert_output_contains "calls the stray version out by name" "does not carry the tagged version" "$wrong"

# --- Unexpected package, correct version: the case the publish wildcard would ship ---
surprise=$(make_artifacts surprise "Surprise.$version.nupkg")
assert_exit "rejects an unexpected package at the tagged version" 1 "$surprise"
assert_output_contains "says the package is not one this release ships" \
  "EXTRA Surprise.$version is not a package this release ships" "$surprise"

# A convincing name is still not an expected one.
lookalike=$(make_artifacts lookalike "Portico.Analyzers.$version.nupkg")
assert_exit "rejects a plausible-looking unexpected package" 1 "$lookalike"

# --- Unexpected symbol package ---
strange_symbols=$(make_artifacts strange-symbols "Surprise.$version.snupkg")
assert_exit "rejects an unexpected symbol package" 1 "$strange_symbols"

# --- A package one directory down does not count as present ---
nested=$(make_artifacts nested)
mkdir -p "$nested/sub"
mv "$nested/Portico.Templates.$version.nupkg" "$nested/sub/"
assert_exit "does not accept a package the publish wildcard cannot reach" 1 "$nested"

# --- No artifacts directory at all ---
assert_exit "rejects a missing artifacts directory" 1 "$workdir/does-not-exist"

echo
echo "  $pass passed, $fail failed"
[[ "$fail" -eq 0 ]] || exit 1
