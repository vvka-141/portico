#!/usr/bin/env bash
# Verify the packed artifacts against the release tag (POR-127).
#
# Usage:  eng/verify-release-artifacts.sh <version> [artifacts-dir]
#
# Asserts that every package Portico ships is present exactly once, at exactly the tagged
# version. Runs BEFORE `dotnet nuget push`, because a nuget.org version is immutable: a
# missing or misversioned package discovered after the push cannot be corrected, only
# superseded. MinVer derives the version from the tag, so a mismatch here means the tag and
# the build disagree — which is precisely the case worth catching while it is still free.

set -euo pipefail

version="${1:?Usage: verify-release-artifacts.sh <version> [artifacts-dir]}"
artifacts="${2:-./artifacts}"

# Every packable project in the solution. A new package must be added here, or the release
# will publish an incomplete set and this script will not notice.
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

for id in "${packages[@]}"; do
  expected="$artifacts/$id.$version.nupkg"
  if [[ -f "$expected" ]]; then
    echo "  OK    $id.$version.nupkg"
  else
    echo "  MISS  expected $id.$version.nupkg" >&2
    # Name what *was* built: nine times out of ten the version is one digit off, and the
    # diagnostic should say so rather than make the reader list the directory themselves.
    found=$(ls "$artifacts/$id".*.nupkg 2>/dev/null | grep -v '\.symbols\.nupkg$' || true)
    if [[ -n "$found" ]]; then
      echo "        found instead: $(echo "$found" | tr '\n' ' ')" >&2
    else
      echo "        no $id package was built at all" >&2
    fi
    failed=true
  fi
done

# A package built at a version other than the tag means MinVer and the tag disagree. Publishing
# that would put an unannounced version on nuget.org under a release that claims another.
while IFS= read -r nupkg; do
  [[ -z "$nupkg" ]] && continue
  base=$(basename "$nupkg" .nupkg)
  if [[ "$base" != *".$version" ]]; then
    echo "  EXTRA $base does not carry the tagged version $version" >&2
    failed=true
  fi
done < <(find "$artifacts" -name '*.nupkg' 2>/dev/null)

if $failed; then
  echo "error: the packed artifacts do not match tag version '$version'" >&2
  exit 1
fi

echo "All ${#packages[@]} packages present at version $version."
