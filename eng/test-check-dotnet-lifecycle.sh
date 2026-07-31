#!/usr/bin/env bash
# Tests for eng/check-dotnet-lifecycle.sh, against FIXTURES rather than the live index.
#
# The radar's whole job is to fire when a target framework enters maintenance. Testing that against
# the real index would make the test's meaning change under it: today net8.0 is in maintenance and
# the "needs a decision" path is exercised, but the day net8.0 is dropped the same test would start
# proving nothing and still pass. All three paths are pinned here instead, so none becomes vacuous.
#
# The fixtures are trimmed copies of the real index (channel-version, release-type, support-phase,
# eol-date), so the parsing under test is the parsing that runs in production.
#
# One invocation per fixture, with several assertions against the captured output — not one
# invocation per assertion. Each run forks bash + curl/python, and nesting enough of them exhausts
# the cygwin heap on a Windows dev machine long before it bothers a Linux runner: the sixth run
# died with "couldn't create signal pipe" while the first five passed. Fewer, richer runs is both
# the fix and the better test.

set -uo pipefail
cd "$(dirname "$0")/.."

script="eng/check-dotnet-lifecycle.sh"
passed=0
failed=0
output=""
exit_code=0

pass() { printf '  PASS  %s\n' "$1"; passed=$((passed + 1)); }
fail() { printf '  FAIL  %s\n' "$1"; failed=$((failed + 1)); }

run_against() {
  output="$(PORTICO_RELEASES_INDEX="$1" bash "$script" 2>&1)"
  exit_code=$?
}

expect_exit() {
  if [ "$exit_code" -eq "$1" ]; then
    pass "$2"
  else
    fail "$2 (expected exit $1, got $exit_code)"
    printf '%s\n' "$output" | sed 's/^/        /'
  fi
}

expect_text() {
  if printf '%s' "$output" | grep -qF "$1"; then
    pass "$2"
  else
    fail "$2 (output did not contain: $1)"
    printf '%s\n' "$output" | sed 's/^/        /'
  fi
}

echo "eng/check-dotnet-lifecycle.sh"

# The live situation as of 2026-07-31: net8.0 in maintenance, EOL 2026-11-10.
run_against eng/fixtures/releases-index.json
expect_exit 3 "a target in maintenance exits 3"
expect_text 'is in **maintenance**'  "  names the phase"
expect_text '2026-11-10'             "  names the end-of-support date"
expect_text '`net10.0`'              "  still reports the healthy target alongside it"

# The other path, which the live index cannot currently exercise at all.
run_against eng/fixtures/releases-index-all-active.json
expect_exit 0 "every target active exits 0"
expect_text 'Every shipped target framework is in active support.' "  says so plainly"

# A radar that reports "all clear" because it could not read anything is worse than no radar, so an
# unreadable index must be exit 1 — never 0, and never mistaken for a finding at 3.
run_against eng/fixtures/releases-index-malformed.json
expect_exit 1 "an unreadable index exits 1, not 0 and not 3"
expect_text 'could not be parsed' "  says why"

printf '\nResults: %d passed, %d failed\n' "$passed" "$failed"
[ "$failed" -eq 0 ]
