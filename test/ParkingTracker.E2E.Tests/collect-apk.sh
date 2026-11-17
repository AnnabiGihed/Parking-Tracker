#!/usr/bin/env bash
# Copies the signed APK that a MAUI publish produced to a fixed path, so that the workflow steps share one lookup.
#
# Usage: collect-apk.sh <publish-directory> <target-apk-path>
#   publish-directory  the -o directory of `dotnet publish`; searched for the first *-Signed.apk
#   target-apk-path    where the APK is copied to; its directory is created when missing
set -euo pipefail

if [[ "$#" -ne 2 ]]; then
  echo "Usage: $0 <publish-directory> <target-apk-path>" >&2
  exit 2
fi

publish_dir="$1"
target="$2"

apk="$(find "$publish_dir" -name '*-Signed.apk' | sort | head -n 1)"
if [[ -z "$apk" ]]; then
  echo "::error::No signed APK found in $publish_dir."
  exit 1
fi

mkdir -p "$(dirname "$target")"
cp "$apk" "$target"
echo "Copied $apk to $target"
