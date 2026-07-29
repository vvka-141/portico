#!/usr/bin/env bash
# Template substitution-token gate (POR-123).
#
# `dotnet new` symbols substitute by literal string replacement across every file in the template
# content. A symbol declared as `"replaces": "net10.0"` therefore rewrites EVERY occurrence of that
# string — a TargetFramework element, yes, but equally a README sentence, a code comment or a
# global.json. It was correct only because no such occurrence happened to exist.
#
# That is a trap that fires on a future edit, silently: `dotnet new portico-cli -f net8.0` would
# quietly produce wrong scaffolded prose, with no error and no failing test. So the tokens are now
# explicit placeholders (TARGET_FRAMEWORK, PORTICO_VERSION) that cannot occur by accident, and this
# script is what stops a real value creeping back in.
#
# Fixing it once without this check would only reset the clock — POR-121 adds a second template to
# the same package, which is exactly the edit most likely to reintroduce it.

set -uo pipefail
cd "$(dirname "$0")/.."

content="templates/Portico.Templates/content"
config_dir=".template.config/"
failures=0

note() { printf '  %s\n' "$1"; }
fail() { printf '\n[FAIL] %s\n' "$1"; failures=$((failures + 1)); }

# A literal value that a symbol replaces must appear ONLY in template.json, where it is the
# choice/default declaration rather than content. Anywhere else it is a substitution target nobody
# intended.
check_literal_absent_from_content() {
  local literal="$1" token="$2"

  local hits
  hits=$(grep -rn --fixed-strings "$literal" "$content" 2>/dev/null \
    | grep -v --fixed-strings "$config_dir" || true)

  if [[ -n "$hits" ]]; then
    fail "The literal '$literal' appears in template content. Use the '$token' placeholder instead — a symbol that replaces '$literal' rewrites every occurrence, including prose."
    printf '%s\n' "$hits" | sed 's/^/    /'
  else
    note "no bare '$literal' in template content (use $token)"
  fi
}

# ...and the placeholder must actually be there, or the substitution silently produces nothing.
check_token_present() {
  local token="$1"

  if grep -rq --fixed-strings "$token" "$content" 2>/dev/null; then
    note "$token is present in template content"
  else
    fail "The placeholder '$token' does not appear anywhere in the template content. A symbol replacing it would be a no-op."
  fi
}

printf 'Checking template substitution tokens...\n'

check_literal_absent_from_content "net10.0" "TARGET_FRAMEWORK"
check_literal_absent_from_content "net8.0" "TARGET_FRAMEWORK"
check_token_present "TARGET_FRAMEWORK"
check_token_present "PORTICO_VERSION"

printf '\n'
if [[ $failures -gt 0 ]]; then
  printf 'Template tokens: %d problem(s)\n' "$failures"
  exit 1
fi

printf 'Template tokens: OK\n'
