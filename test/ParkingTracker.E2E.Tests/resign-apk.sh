#!/usr/bin/env bash
# Re-signs an APK with another key, so that two builds of the same app can be installed over each other (the upgrade test).
# apksigner replaces the old signature; zipalign runs first because it would break a signature made before it.
#
# Usage: resign-apk.sh <input-apk> <output-apk> <keystore> <key-alias>
#   keystore password and key password: both read from the environment variable UPGRADE_KEY_PASSWORD (never from the arguments, so
#   that they do not show in the process list or the log)
#   needs ANDROID_HOME with the build-tools of an installed Android SDK
set -euo pipefail

if [ "$#" -ne 4 ]; then
  echo "Usage: $0 <input-apk> <output-apk> <keystore> <key-alias>" >&2
  exit 2
fi
if [ -z "${UPGRADE_KEY_PASSWORD:-}" ] || [ -z "${ANDROID_HOME:-}" ]; then
  echo "::error::UPGRADE_KEY_PASSWORD and ANDROID_HOME must be set."
  exit 2
fi

input="$1"
output="$2"
keystore="$3"
alias_name="$4"

tools="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
if [ -z "$tools" ]; then
  echo "::error::No Android build-tools found in $ANDROID_HOME."
  exit 1
fi

aligned="$(mktemp --suffix=.apk)"
trap 'rm -f "$aligned"' EXIT
mkdir -p "$(dirname "$output")"
"$tools/zipalign" -f -p 4 "$input" "$aligned"
"$tools/apksigner" sign --ks "$keystore" --ks-key-alias "$alias_name" \
  --ks-pass env:UPGRADE_KEY_PASSWORD --key-pass env:UPGRADE_KEY_PASSWORD --out "$output" "$aligned"
"$tools/apksigner" verify --print-certs "$output" | grep -E "Signer #1 certificate SHA-256"
echo "Signed $input as $output"
