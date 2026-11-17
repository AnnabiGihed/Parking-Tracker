#!/usr/bin/env bash
# Fails when the new APK is signed with another certificate than the APK of the latest GitHub release. Android refuses to
# update an app whose signing certificate changed, so a wrong key must stop the workflow before it publishes.
#
# Usage: certificate-guard.sh <new-signed-apk>
#   needs ANDROID_HOME, and GH_TOKEN and GH_REPO for the GitHub CLI (the release APK is ParkingTracker.apk)
# Every failure is reported as a ::error:: line on stderr.
set -euo pipefail

GUARD_NAME="certificate-guard.sh"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=tools/ci/common.sh
. "$here/common.sh"

[[ "$#" -eq 1 ]] || fail "Usage: $0 <new-signed-apk>" 2
[[ -f "$1" ]] || fail "APK '$1' does not exist."

tools="$(build_tools_dir)"
[[ -x "$tools/apksigner" ]] || fail "apksigner not found or not executable in '$tools'."

# Prints the SHA-256 digest of the first signer certificate of an APK. The output is captured first: with pipefail a grep
# that does not match would end the script silently.
certificate_digest() {
  local output line
  output="$("$tools/apksigner" verify --print-certs "$1" 2>&1)" || {
    echo "$output" >&2
    fail "apksigner verify rejected '$1'."
  }
  line="$(printf '%s\n' "$output" | grep -i "certificate SHA-256 digest" | head -n 1 || true)"
  [[ -n "$line" ]] || fail "apksigner printed no certificate SHA-256 digest for '$1'."
  printf '%s\n' "${line##*: }" | tr -d '[:space:]' | tr 'A-F' 'a-f'
}

tag="$(latest_release_tag)"
if [[ -z "$tag" ]]; then
  echo "No release exists yet; the certificate needs no comparison."
  exit 0
fi

download="$(mktemp -d)"
trap 'rm -rf "$download"' EXIT
gh release download "$tag" --pattern "ParkingTracker.apk" --dir "$download" || fail "Could not download ParkingTracker.apk of release $tag."
[[ -f "$download/ParkingTracker.apk" ]] || fail "Release $tag has no ParkingTracker.apk."

old="$(certificate_digest "$download/ParkingTracker.apk")"
new="$(certificate_digest "$1")"
echo "Release $tag certificate SHA-256: $old"
echo "New APK certificate SHA-256:     $new"
if [[ "$old" != "$new" ]]; then
  fail "The new APK is signed with another certificate than release $tag; the update would be refused on the phones."
fi
echo "The signing certificate is the same."
