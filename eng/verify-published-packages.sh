#!/usr/bin/env bash
# Confirm every shipped package is actually live on nuget.org (POR-141).
#
# Usage:  eng/verify-published-packages.sh <version> [timeout-seconds]
#
# `dotnet nuget push` returning 201 means nuget.org accepted the upload, not that the package
# exists. Validation — signing, malware scanning — runs afterwards and can reject it, and a
# rejected package is invisible: the package page, the registration index and the search service
# all 404, exactly as they do for an ID that was never pushed.
#
# This is the last gate before the GitHub Release is created, because release notes that promise
# four packages when one shipped are worse than no release at all. v0.1.0 shipped that way.
#
# Validation lag is normal and uneven: on the 0.1.0 release the core package resolved in about
# three minutes while the others never did. Hence a poll rather than a single check, and hence a
# timeout that fails rather than waits forever.

set -euo pipefail

version="${1:?Usage: verify-published-packages.sh <version> [timeout-seconds]}"
timeout_seconds="${2:-900}"
poll_seconds=15

# Lower-cased: the flat container is case-sensitive and serves lowercase ids.
packages=(
  "portico"
  "portico.dependencyinjection"
  "portico.hosting"
  "portico.templates"
)

feed="https://api.nuget.org/v3-flatcontainer"

is_live() {
  local id="$1"
  curl -sf "$feed/$id/index.json" 2>/dev/null | tr -d ' \n' | grep -qF "\"$version\""
}

echo "Waiting for $version to become resolvable on nuget.org (timeout ${timeout_seconds}s)."

deadline=$((SECONDS + timeout_seconds))
pending=("${packages[@]}")

while true; do
  still_pending=()
  for id in "${pending[@]}"; do
    if is_live "$id"; then
      echo "  LIVE  $id $version"
    else
      still_pending+=("$id")
    fi
  done
  pending=("${still_pending[@]+"${still_pending[@]}"}")

  [[ ${#pending[@]} -eq 0 ]] && break

  if (( SECONDS >= deadline )); then
    echo "error: these packages never became resolvable at $version:" >&2
    for id in "${pending[@]}"; do
      echo "  MISSING  $id" >&2
    done
    echo >&2
    echo "A package that was accepted but never appears has failed validation, or its id is" >&2
    echo "unavailable. Check https://www.nuget.org/account/Packages and the account's email." >&2
    exit 1
  fi

  echo "  ... ${#pending[@]} still validating (${pending[*]})"
  sleep "$poll_seconds"
done

echo "All ${#packages[@]} packages are live at $version."
