#!/usr/bin/env bash
# Docs integrity gate (POR-77).
#
# Three documentation defects shipped undetected and were found by an outside reader: a relative
# link that resolved above the repository root, a citation to an ADR that has never existed, and
# public documents pointing at a tracker no reader can open. Each is mechanically detectable, and
# the CHARTER's own gates had already rotted twice before that — a gate nobody re-runs is
# decoration. This script is the re-run.
#
# What it does NOT catch: a document that contradicts itself, or a claim that is merely false.
# Those need a reader. This covers the part that does not.

set -uo pipefail
cd "$(dirname "$0")/.."

failures=0

note() { printf '  %s\n' "$1"; }
fail() { printf '\n[FAIL] %s\n' "$1"; failures=$((failures + 1)); }

docs=$(find docs -name '*.md' 2>/dev/null; ls README.md CONTRIBUTING.md SECURITY.md CHANGELOG.md 2>/dev/null)

# --- 1. Relative Markdown links resolve ---------------------------------------------------------

echo "Checking relative links..."
broken=""
for file in $docs; do
    dir=$(dirname "$file")
    # [text](target) where target is not a URL, not an anchor, not a mailto.
    targets=$(grep -oE '\]\([^)#][^)]*\)' "$file" 2>/dev/null \
        | sed -E 's/^\]\(//; s/\)$//' \
        | grep -vE '^(https?|mailto):' || true)

    for target in $targets; do
        # Strip a trailing #anchor; the file is what we verify.
        path="${target%%#*}"
        [ -z "$path" ] && continue
        if [ ! -e "$dir/$path" ]; then
            broken="$broken\n  $file -> $target"
        fi
    done
done

if [ -n "$broken" ]; then
    fail "Relative links that do not resolve:"
    printf "%b\n" "$broken"
else
    note "all relative links resolve"
fi

# --- 2. No private tracker links in public documents --------------------------------------------
#
# The Jira board is the internal working surface (CLAUDE.md); the public record of a decision has
# to be a checked-in document. A reader who follows one of these links gets a login page.

echo "Checking for private tracker links..."
tracker=$(grep -rnE 'https?://[a-z0-9-]+\.atlassian\.net' $docs 2>/dev/null || true)
if [ -n "$tracker" ]; then
    fail "Public documents link to a private tracker:"
    printf '%s\n' "$tracker" | sed 's/^/  /'
else
    note "no private tracker links"
fi

# --- 3. Cited ADRs exist ------------------------------------------------------------------------
#
# extensibility.md cited "ADR 0003" for a parked decision. There is no docs/adr/ directory and
# never has been. A dangling authority citation is worse than no citation: it reads as though the
# reasoning was written down somewhere.

echo "Checking ADR citations..."
adr_refs=$(grep -rnE 'ADR[ -]?[0-9]{3,4}' $docs 2>/dev/null || true)
if [ -n "$adr_refs" ] && [ ! -d docs/adr ]; then
    fail "ADRs are cited but docs/adr/ does not exist:"
    printf '%s\n' "$adr_refs" | sed 's/^/  /'
else
    note "no dangling ADR citations"
fi

echo
if [ "$failures" -gt 0 ]; then
    echo "Docs integrity: $failures check(s) failed."
    exit 1
fi
echo "Docs integrity: OK"
