#!/usr/bin/env bash
# Extract a single version section from CHANGELOG.md (POR-99).
#
# Usage:  eng/extract-changelog-section.sh <version> [changelog-path]
#
# Prints the body of the ## [<version>] section to stdout — everything between that heading
# and the next ## heading (or EOF), with leading/trailing blank lines trimmed.
#
# Exits 1 with a diagnostic to stderr if the version section does not exist.
# This is the failure mode that matters: an empty or absent section at tag time means the
# GitHub Release ships with no notes, and nobody notices until a visitor arrives.
#
# The release workflow calls this to populate --notes-file; it is also tested standalone
# by eng/test-extract-changelog-section.sh.

set -euo pipefail

version="${1:?Usage: extract-changelog-section.sh <version> [changelog-path]}"
changelog="${2:-CHANGELOG.md}"

if [[ ! -f "$changelog" ]]; then
  echo "error: changelog not found: $changelog" >&2
  exit 1
fi

heading="## [$version]"

in_section=false
body=""

while IFS= read -r line; do
  if [[ "$line" == "$heading"* ]]; then
    in_section=true
    continue
  fi
  if $in_section; then
    if [[ "$line" == "## ["* ]]; then
      break
    fi
    body+="$line"$'\n'
  fi
done < "$changelog"

if ! $in_section; then
  echo "error: no section found for version '$version' in $changelog" >&2
  exit 1
fi

# Trim leading and trailing blank lines
trimmed=$(echo "$body" | sed '/./,$!d' | sed -e :a -e '/^\n*$/{$d;N;ba}')

if [[ -z "$trimmed" ]]; then
  echo "error: section for version '$version' exists but is empty in $changelog" >&2
  exit 1
fi

echo "$trimmed"
