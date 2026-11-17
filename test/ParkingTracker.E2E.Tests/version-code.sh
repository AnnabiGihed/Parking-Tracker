#!/usr/bin/env bash
# Prints the versionCode of an APK.
#
# Usage: version-code.sh <apk>
#   needs ANDROID_HOME with the build-tools of an installed Android SDK
# Every failure is reported as a ::error:: line on stderr.
set -euo pipefail

fail() {
  echo "::error::version-code.sh: $1" >&2
  exit "${2:-1}"
}

[[ "$#" -eq 1 ]] || fail "Usage: $0 <apk>" 2
[[ -n "${ANDROID_HOME:-}" ]] || fail "ANDROID_HOME must be set." 2
[[ -d "$ANDROID_HOME/build-tools" ]] || fail "No build-tools directory in '$ANDROID_HOME'."
[[ -f "$1" ]] || fail "APK '$1' does not exist."

tools="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
[[ -n "$tools" ]] || fail "No Android build-tools version found in '$ANDROID_HOME/build-tools'."
[[ -x "$tools/aapt2" ]] || fail "aapt2 not found or not executable in '$tools'."

badging="$("$tools/aapt2" dump badging "$1")" || fail "aapt2 dump badging failed for '$1'."
code="$(printf '%s\n' "$badging" | sed -n "s/^package: .*versionCode='\([0-9]*\)'.*/\1/p" | head -n 1)"
[[ -n "$code" ]] || fail "No versionCode found in the badging of '$1'."
echo "$code"
