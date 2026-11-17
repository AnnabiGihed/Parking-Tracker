#!/usr/bin/env bash
# Fails when the versionCode of the new APK is not higher than the version of the latest GitHub release, because Android
# refuses to install an update with a lower or equal versionCode. The release tag v1.0.N carries N, which is the
# versionCode the workflow gave that release (the run number).
#
# Usage: version-guard.sh <new-apk>
#   needs ANDROID_HOME, and GH_TOKEN and GH_REPO for the GitHub CLI
# Every failure is reported as a ::error:: line on stderr.
set -euo pipefail

GUARD_NAME="version-guard.sh"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=tools/ci/common.sh
. "$here/common.sh"

[[ "$#" -eq 1 ]] || fail "Usage: $0 <new-apk>" 2
[[ -f "$1" ]] || fail "APK '$1' does not exist."

new="$(bash "$here/../../test/ParkingTracker.E2E.Tests/version-code.sh" "$1")"
tag="$(latest_release_tag)"
if [[ -z "$tag" ]]; then
  echo "No release exists yet; versionCode $new needs no comparison."
  exit 0
fi

if [[ ! "$tag" =~ ^v1\.0\.([0-9]+)$ ]]; then
  fail "The latest release tag '$tag' does not have the form v1.0.N."
fi
old="${BASH_REMATCH[1]}"

echo "Latest release $tag (versionCode $old); new versionCode $new."
if [[ "$new" -le "$old" ]]; then
  fail "The new versionCode ($new) must be higher than the versionCode of the latest release $tag ($old)."
fi
echo "The new versionCode is higher."
