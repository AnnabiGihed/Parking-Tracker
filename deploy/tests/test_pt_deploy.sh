#!/usr/bin/env bash
# Tests of the command parsing in deploy/server/pt-deploy and pt-deploy-root, and of the job window.
# Run: bash deploy/tests/test_pt_deploy.sh   Exit code 0 when every test passes, 1 otherwise.
set -uo pipefail

SERVER_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/../server" && pwd)
# shellcheck source=../server/pt-deploy
source "$SERVER_DIR/pt-deploy"
# shellcheck source=../server/pt-deploy-root
source "$SERVER_DIR/pt-deploy-root"
set +e

failures=0
passes=0

check() {
	local description=$1 expected=$2
	shift 2
	"$@" >/dev/null 2>&1
	local status=$?
	if { [[ $expected == accept ]] && ((status == 0)); } || { [[ $expected == refuse ]] && ((status != 0)); }; then
		passes=$((passes + 1))
	else
		failures=$((failures + 1))
		echo "FAIL: $description (expected $expected, exit $status)" >&2
	fi
}

accepted_commands=(
	"deploy sha-0123456789ab"
	"deploy sha-0123456789ab force"
	"deploy sha-0123456789ab drill"
	"deploy sha-0123456789ab force drill"
	"rollback"
	"status"
)
refused_commands=(
	""
	"deploy"
	"deploy sha-0123456789a"
	"deploy sha-0123456789abc"
	"deploy sha-0123456789AB"
	"deploy main"
	"deploy sha-0123456789ab now"
	"deploy sha-0123456789ab force force"
	"deploy sha-0123456789ab; rm -rf /"
	"deploy sha-0123456789ab && id"
	"deploy \$(id)"
	"rollback now"
	"status --all"
	"bash"
	"scp -t /tmp"
)

for command in "${accepted_commands[@]}"; do
	check "pt-deploy accepts '$command'" accept parse_command "$command"
	# shellcheck disable=SC2086 # the helper receives the words one by one, as pt-deploy passes them
	check "pt-deploy-root accepts '$command'" accept valid_arguments $command
done
for command in "${refused_commands[@]}"; do
	check "pt-deploy refuses '$command'" refuse parse_command "$command"
done
check "pt-deploy-root refuses no arguments" refuse valid_arguments
check "pt-deploy-root refuses a bad sha" refuse valid_arguments deploy sha-xyz
check "pt-deploy-root refuses an unknown option" refuse valid_arguments deploy sha-0123456789ab now
check "pt-deploy-root refuses a repeated option" refuse valid_arguments deploy sha-0123456789ab drill drill
check "pt-deploy-root refuses one word with spaces" refuse valid_arguments "deploy sha-0123456789ab"
check "pt-deploy-root refuses a shell metacharacter" refuse valid_arguments deploy 'sha-0123456789ab;id'

for hhmm in 0230 0300 0459; do
	check "$hhmm is in the job window" accept in_job_window "$hhmm"
done
for hhmm in 0000 0229 0500 0800 2359; do
	check "$hhmm is outside the job window" refuse in_job_window "$hhmm"
done

# Regression: clean_up after a first release (no previous one, nothing to remove) must succeed under set -e and
# pipefail; it once returned grep's "no match" status and failed a deployment that had gone live.
first_release_clean_up() {
	(
		set -euo pipefail
		release_of_link() { [[ $1 == current ]] && echo sha-0123456789ab; return 0; }
		docker() { return 0; }
		clean_up
	)
}
check "clean_up succeeds after a first release" accept first_release_clean_up

echo "pt-deploy tests: $passes passed, $failures failed"
((failures == 0))
