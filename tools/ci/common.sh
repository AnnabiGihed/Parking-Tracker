#!/usr/bin/env bash
# Shared helpers of the release guards (version-guard.sh, certificate-guard.sh). Source it; it runs nothing by itself.
#   needs ANDROID_HOME with the build-tools of an installed Android SDK, and the GitHub CLI with GH_TOKEN and GH_REPO set
# Every failure is reported as a ::error:: line on stderr.

# Reports a failure and ends the script: fail <message> [exit code].
fail() {
  echo "::error::${GUARD_NAME:-ci}: $1" >&2
  exit "${2:-1}"
}

# Prints the newest Android build-tools directory (the same lookup as test/ParkingTracker.E2E.Tests/version-code.sh).
build_tools_dir() {
  [ -n "${ANDROID_HOME:-}" ] || fail "ANDROID_HOME must be set." 2
  [ -d "$ANDROID_HOME/build-tools" ] || fail "No build-tools directory in '$ANDROID_HOME'."
  local tools
  tools="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
  [ -n "$tools" ] || fail "No Android build-tools version found in '$ANDROID_HOME/build-tools'."
  echo "$tools"
}

# Prints the tag of the latest GitHub release, or nothing when the repository has no release yet.
latest_release_tag() {
  [ -n "${GH_TOKEN:-}" ] || fail "GH_TOKEN must be set." 2
  [ -n "${GH_REPO:-}" ] || fail "GH_REPO must be set." 2
  gh release list --exclude-drafts --exclude-pre-releases --limit 30 --json tagName,isLatest \
    --jq '[.[] | select(.isLatest)][0].tagName // empty' || fail "gh release list failed."
}
