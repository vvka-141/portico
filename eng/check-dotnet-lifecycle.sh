#!/usr/bin/env bash
# .NET lifecycle radar.
#
# Nothing automates the choice of a target framework, and nothing should — dropping a TFM is a
# judgement call about who you are willing to stop shipping to. What CAN be automated is the ALERT,
# so the decision arrives on the board with months of warning instead of being discovered by a
# consumer whose runtime stopped getting security fixes (POR-146).
#
# Dependabot covers actions, NuGet packages and (since this ticket) the SDK in global.json. It does
# not touch <TargetFrameworks>, and neither does Renovate. This script is what fills that gap.
#
# What it does NOT do: it does not edit anything, does not open the issue itself (the workflow does
# that, so this stays runnable locally), and does not judge whether a target should be dropped. It
# reports support phase against the authoritative index and leaves the decision where it belongs.
#
# Exit codes are the interface:
#   0  every shipped target is still in active support — nothing to report
#   3  at least one target is in maintenance or EOL, or a newer LTS has gone GA
#   1  the script could not answer (no TFMs found, index unreachable, malformed index)
#
# 3 rather than 1 for "needs attention" so a real failure is never mistaken for a finding. This is
# deliberately NOT wired into verify.yml: net8.0 being in maintenance is a decision to schedule, not
# a build break.
#
# Override the index for testing:
#   PORTICO_RELEASES_INDEX=eng/fixtures/releases-index.json bash eng/check-dotnet-lifecycle.sh

set -uo pipefail
cd "$(dirname "$0")/.."

INDEX_SOURCE="${PORTICO_RELEASES_INDEX:-https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json}"

command -v python3 >/dev/null 2>&1 || { echo "python3 is required to read the release index." >&2; exit 1; }

# Shipped targets: the solution-wide default plus any project that overrides it. netstandard2.0 (the
# analyzers, which Roslyn requires) has no channel in the index and is filtered out below.
tfms="$(
  grep -ho '<TargetFrameworks\{0,1\}>[^<]*</TargetFrameworks\{0,1\}>' \
    Directory.Build.props src/*/*.csproj 2>/dev/null |
    sed -e 's/<[^>]*>//g' |
    tr ';' '\n' |
    tr -d '[:blank:]' |
    grep -E '^net[0-9]+\.[0-9]+$' |
    sort -u
)"

if [ -z "$tfms" ]; then
  echo "No net<major>.<minor> target frameworks found. If the TFM moved out of Directory.Build.props," >&2
  echo "update this script — do not delete the radar." >&2
  exit 1
fi

index_json="$(mktemp)"
trap 'rm -f "$index_json"' EXIT

if [ -f "$INDEX_SOURCE" ]; then
  cp "$INDEX_SOURCE" "$index_json"
elif ! curl -sSfL --max-time 60 "$INDEX_SOURCE" -o "$index_json"; then
  echo "Could not fetch the .NET release index from $INDEX_SOURCE" >&2
  exit 1
fi

python3 - "$index_json" $tfms <<'PY'
import json, sys

index_path, *tfms = sys.argv[1:]

try:
    channels = json.load(open(index_path, encoding="utf-8"))["releases-index"]
except Exception as error:                                    # noqa: BLE001 - reported, not raised
    print(f"The release index could not be parsed: {error}", file=sys.stderr)
    sys.exit(1)

by_channel = {c["channel-version"]: c for c in channels}

# "active" is the only phase that needs no attention. preview/go-live cannot appear for a shipped
# target, and if one ever did, saying so is the correct surprise.
ATTENTION = {"maintenance", "eol"}

findings, rows, unknown = [], [], []

for tfm in tfms:
    channel = tfm.removeprefix("net")
    entry = by_channel.get(channel)

    if entry is None:
        unknown.append(tfm)
        continue

    phase = entry.get("support-phase", "unknown")
    eol = entry.get("eol-date") or "—"
    rows.append(f"| `{tfm}` | {entry.get('release-type', '?').upper()} | **{phase}** | {eol} |")

    if phase in ATTENTION:
        findings.append(f"`{tfm}` is in **{phase}** (end of support {eol})")

# A newer LTS reaching GA is the other thing worth knowing about, because it is the target you would
# add. Preview channels are not GA and are deliberately not reported.
shipped = {t.removeprefix("net") for t in tfms}
newer_lts = [
    c["channel-version"] for c in channels
    if c.get("release-type") == "lts"
    and c.get("support-phase") == "active"
    and c["channel-version"] not in shipped
]
for channel in newer_lts:
    findings.append(f"`net{channel}` is an **active LTS** that Portico does not target")

print("| Target | Track | Support phase | End of support |")
print("|---|---|---|---|")
for row in rows:
    print(row)

if unknown:
    print()
    print("Not found in the release index: " + ", ".join(f"`{t}`" for t in unknown))

if findings:
    print()
    print("**Needs a decision:**")
    print()
    for finding in findings:
        print(f"- {finding}")
    print()
    print("Dropping or adding a target framework is a judgement call, not something to automate —")
    print("this radar exists to make sure the call is made on time, not to make it. Note that")
    print("`Portico_MultiTargeting_Should` pins the current TFM set and will fail until it and")
    print("`CLAUDE.md` are updated together, which is intended.")
    sys.exit(3)

print()
print("Every shipped target framework is in active support.")
PY
