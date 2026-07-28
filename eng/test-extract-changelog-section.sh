#!/usr/bin/env bash
# Tests for eng/extract-changelog-section.sh (POR-99).
#
# Uses a fixture changelog that covers:
#   - A released version with content (must succeed)
#   - The Unreleased heading (must succeed)
#   - A version that does not exist (must fail loudly)
#   - A missing file (must fail loudly)

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
extract="$script_dir/extract-changelog-section.sh"

pass=0
fail=0

assert_exit() {
  local label="$1" expected_exit="$2"
  shift 2
  local actual_exit=0
  "$@" > /dev/null 2>&1 || actual_exit=$?
  if [[ "$actual_exit" -eq "$expected_exit" ]]; then
    echo "  PASS  $label"
    pass=$((pass + 1))
  else
    echo "  FAIL  $label (expected exit $expected_exit, got $actual_exit)"
    fail=$((fail + 1))
  fi
}

assert_output_contains() {
  local label="$1" needle="$2"
  shift 2
  local output
  output=$("$@" 2>&1) || true
  if echo "$output" | grep -qF "$needle"; then
    echo "  PASS  $label"
    pass=$((pass + 1))
  else
    echo "  FAIL  $label (output did not contain '$needle')"
    echo "        got: $output"
    fail=$((fail + 1))
  fi
}

# --- Fixture ---
fixture=$(mktemp)
trap 'rm -f "$fixture"' EXIT

cat > "$fixture" << 'FIXTURE'
# Changelog

## [Unreleased]

### Added

- Something coming soon.

## [0.2.0] - 2026-08-15

### Fixed

- Fixed a bug in the parser.

### Added

- New feature X.

## [0.1.0] - 2026-07-28

### Added

- Initial release: the framework, the analyzers, the adapters, the template.

### Fixed

- `--opt=value` now binds.

[Unreleased]: https://github.com/example/repo/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/example/repo/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/example/repo/releases/tag/v0.1.0
FIXTURE

echo "extract-changelog-section tests"
echo "================================"

# 1. Extract a released version
assert_exit "extracts 0.1.0 section" 0 bash "$extract" "0.1.0" "$fixture"
assert_output_contains "0.1.0 contains initial release line" "Initial release" bash "$extract" "0.1.0" "$fixture"
assert_output_contains "0.1.0 contains --opt=value fix" "opt=value" bash "$extract" "0.1.0" "$fixture"

# 2. Extract another released version
assert_exit "extracts 0.2.0 section" 0 bash "$extract" "0.2.0" "$fixture"
assert_output_contains "0.2.0 contains parser fix" "parser" bash "$extract" "0.2.0" "$fixture"

# 3. Extract the Unreleased section
assert_exit "extracts Unreleased section" 0 bash "$extract" "Unreleased" "$fixture"
assert_output_contains "Unreleased contains coming soon" "coming soon" bash "$extract" "Unreleased" "$fixture"

# 4. Version that does not exist — must fail
assert_exit "fails on nonexistent version" 1 bash "$extract" "9.9.9" "$fixture"
assert_output_contains "error message names the version" "no section found for version '9.9.9'" bash "$extract" "9.9.9" "$fixture"

# 5. Missing file — must fail
assert_exit "fails on missing file" 1 bash "$extract" "0.1.0" "/nonexistent/CHANGELOG.md"

# 6. No arguments — must fail
assert_exit "fails with no arguments" 1 bash "$extract"

# 7. Extracted content does not include the heading itself
output=$(bash "$extract" "0.1.0" "$fixture" 2>&1)
if echo "$output" | grep -qF "## [0.1.0]"; then
  echo "  FAIL  output must not include the heading line"
  fail=$((fail + 1))
else
  echo "  PASS  output does not include the heading line"
  pass=$((pass + 1))
fi

# 8. Extracted content does not include the next version's content
if echo "$output" | grep -qF "parser"; then
  echo "  FAIL  output must not bleed into next section"
  fail=$((fail + 1))
else
  echo "  PASS  output does not bleed into next section"
  pass=$((pass + 1))
fi

echo ""
echo "Results: $pass passed, $fail failed"
[[ "$fail" -eq 0 ]]
