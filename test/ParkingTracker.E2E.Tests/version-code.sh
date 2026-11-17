#!/usr/bin/env bash
# Prints the versionCode of an APK.
#
# Usage: version-code.sh <apk>
#   needs ANDROID_HOME with the build-tools of an installed Android SDK
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 <apk>" >&2
  exit 2
fi
if [ -z "${ANDROID_HOME:-}" ]; then
  echo "::error::ANDROID_HOME must be set."
  exit 2
fi

tools="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
"$tools/aapt2" dump badging "$1" | sed -n "s/^package: .*versionCode='\([0-9]*\)'.*/\1/p" | head -n 1
