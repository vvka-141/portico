#!/usr/bin/env bash
# Verify the packed artifacts against the release tag (POR-127, POR-132).
#
# Usage:  eng/verify-release-artifacts.sh <version> [artifacts-dir]
#
# Asserts that the packages about to be published are *exactly* the set Portico ships, at
# exactly the tagged version — nothing missing, nothing extra. Runs BEFORE `dotnet nuget push`,
# because a nuget.org version is immutable: a missing or misversioned package discovered after
# the push cannot be corrected, only superseded. MinVer derives the version from the tag, so a
# mismatch here means the tag and the build disagree, which is precisely the case worth catching
# while it is still free.
#
# The set has to be checked in both directions. `dotnet nuget push` publishes a wildcard, so a
# package that nobody expected is as much of a release defect as one that is missing: it would
# reach nuget.org under the Portico release with no one having decided to ship it.

set -euo pipefail

version="${1:?Usage: verify-release-artifacts.sh <version> [artifacts-dir]}"
artifacts="${2:-./artifacts}"

# Every packable project in the solution. A new package must be added here, or the release will
# stop — which is the intended failure: the list is the decision about what Portico publishes.
packages=(
  "Portico"
  "Portico.DependencyInjection"
  "Portico.Hosting"
  "Portico.Templates"
)

if [[ ! -d "$artifacts" ]]; then
  echo "error: artifacts directory not found: $artifacts" >&2
  exit 1
fi

failed=false

# Whether $1 is one of the package ids above.
is_known_package() {
  local candidate="$1" id
  for id in "${packages[@]}"; do
    [[ "$candidate" == "$id" ]] && return 0
  done
  return 1
}

for id in "${packages[@]}"; do
  expected="$artifacts/$id.$version.nupkg"
  if [[ -f "$expected" ]]; then
    echo "  OK    $id.$version.nupkg"
  else
    echo "  MISS  expected $id.$version.nupkg" >&2
    # Name what *was* built: nine times out of ten the version is one digit off, and the
    # diagnostic should say so rather than make the reader list the directory themselves.
    found=$(ls "$artifacts/$id".*.nupkg 2>/dev/null || true)
    if [[ -n "$found" ]]; then
      echo "        found instead: $(echo "$found" | tr '\n' ' ')" >&2
    else
      echo "        no $id package was built at all" >&2
    fi
    failed=true
  fi
done

# The other direction. Only the top level is scanned, because that is exactly what the publish
# wildcard `./artifacts/*.nupkg` reaches — a stray package one directory down is not a release
# hazard, and a *wanted* package one directory down is already a MISS above.
while IFS= read -r nupkg; do
  [[ -z "$nupkg" ]] && continue
  base=$(basename "$nupkg" .nupkg)

  # "Portico.Hosting.0.1.0" splits into an id and a version at the last dash-free boundary the
  # id list agrees with, so match against the ids rather than guessing where the version starts.
  matched=false
  for id in "${packages[@]}"; do
    if [[ "$base" == "$id.$version" ]]; then
      matched=true
      break
    fi
  done
  $matched && continue

  id="${base%.*}"
  while [[ -n "$id" ]] && ! is_known_package "$id"; do
    next="${id%.*}"
    [[ "$next" == "$id" ]] && break
    id="$next"
  done

  if is_known_package "$id"; then
    echo "  EXTRA $base does not carry the tagged version $version" >&2
  else
    echo "  EXTRA $base is not a package this release ships" >&2
    echo "        add it to the package list in $(basename "$0") or keep it out of $artifacts" >&2
  fi
  failed=true
done < <(find "$artifacts" -maxdepth 1 -name '*.nupkg' 2>/dev/null | sort)

# Symbol packages ride along with their .nupkg on push, so they get the same treatment. Absence
# is fine — Portico.Templates sets IncludeSymbols=false — but a symbol package for something
# Portico does not ship, or at another version, is the same defect one file extension over.
while IFS= read -r snupkg; do
  [[ -z "$snupkg" ]] && continue
  base=$(basename "$snupkg" .snupkg)
  matched=false
  for id in "${packages[@]}"; do
    if [[ "$base" == "$id.$version" ]]; then
      matched=true
      break
    fi
  done
  if ! $matched; then
    echo "  EXTRA $base.snupkg is not a symbol package this release ships at version $version" >&2
    failed=true
  fi
done < <(find "$artifacts" -maxdepth 1 -name '*.snupkg' 2>/dev/null | sort)

if $failed; then
  echo "error: the packed artifacts do not match tag version '$version'" >&2
  exit 1
fi

echo "All ${#packages[@]} packages present at version $version, and nothing else."
