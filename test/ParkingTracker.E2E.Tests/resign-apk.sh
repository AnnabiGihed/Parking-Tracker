#!/usr/bin/env bash
# Re-signs an APK with another key, so that two builds of the same app can be installed over each other (the upgrade test).
# apksigner replaces the old signature; zipalign runs first because it would break a signature made before it.
#
# Usage: resign-apk.sh <input-apk> <output-apk> <keystore> <key-alias>
#   keystore password and key password: both read from the environment variable UPGRADE_KEY_PASSWORD (never from the arguments, so
#   that they do not show in the process list or the log)
#   needs ANDROID_HOME with the build-tools of an installed Android SDK
# Every stage is echoed and every failure is reported as a ::error:: line on stderr, so that a failed run is never silent.
set -euo pipefail

fail() {
  echo "::error::resign-apk.sh: $1" >&2
  exit "${2:-1}"
}
stage() {
  echo "resign-apk.sh: $1"
}
# Runs a command and reports it by name when it fails; its own output stays visible (stderr is merged into the log).
run() {
  local name="$1"
  shift
  stage "$name"
  "$@" || fail "$name failed with exit code $?."
}
trap 'fail "unexpected error at line $LINENO (exit code $?)."' ERR

[[ "$#" -eq 4 ]] || fail "Usage: $0 <input-apk> <output-apk> <keystore> <key-alias>" 2
input="$1"
output="$2"
keystore="$3"
alias_name="$4"

[[ -n "${UPGRADE_KEY_PASSWORD:-}" ]] || fail "UPGRADE_KEY_PASSWORD must be set (job-level env)." 2
[[ -n "${ANDROID_HOME:-}" ]] || fail "ANDROID_HOME must be set." 2
[[ -d "$ANDROID_HOME" ]] || fail "ANDROID_HOME '$ANDROID_HOME' is not a directory."
[[ -d "$ANDROID_HOME/build-tools" ]] || fail "No build-tools directory in '$ANDROID_HOME'."
[[ -f "$input" ]] || fail "Input APK '$input' does not exist."
[[ -f "$keystore" ]] || fail "Keystore '$keystore' does not exist."

tools="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
[[ -n "$tools" ]] || fail "No Android build-tools version found in '$ANDROID_HOME/build-tools'."
[[ -x "$tools/zipalign" ]] || fail "zipalign not found or not executable in '$tools'."
[[ -x "$tools/apksigner" ]] || fail "apksigner not found or not executable in '$tools'."
stage "using build-tools '$tools'"

aligned="$(mktemp)"
trap 'rm -f "$aligned"' EXIT
mkdir -p "$(dirname "$output")"
# zipalign gets a path that does not exist yet, so that it never works on the empty placeholder file that mktemp creates.
rm -f "$aligned"

run "zipalign '$input'" "$tools/zipalign" -f -p 4 "$input" "$aligned"
[[ -s "$aligned" ]] || fail "zipalign wrote no output."
run "apksigner sign with alias '$alias_name'" "$tools/apksigner" sign --ks "$keystore" --ks-key-alias "$alias_name" \
  --ks-pass env:UPGRADE_KEY_PASSWORD --key-pass env:UPGRADE_KEY_PASSWORD --out "$output" "$aligned"
[[ -s "$output" ]] || fail "apksigner wrote no output '$output'."

# Capture first: with pipefail a grep that does not match would end the script silently, and the wording of the certificate lines
# differs between build-tools versions (for example "Signer #1 certificate SHA-256 digest" and "Signer (minSdkVersion=24, ...)
# certificate SHA-256 digest").
stage "apksigner verify '$output'"
verification="$("$tools/apksigner" verify --verbose --print-certs "$output" 2>&1)" || {
  echo "$verification" >&2
  fail "apksigner verify rejected '$output'."
}
echo "$verification"
digest="$(printf '%s\n' "$verification" | grep -i "certificate SHA-256 digest" | head -n 1 || true)"
[[ -n "$digest" ]] || fail "apksigner verify printed no certificate SHA-256 digest for '$output'."
stage "signed '$input' as '$output': $digest"
