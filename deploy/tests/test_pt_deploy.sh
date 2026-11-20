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
		release_of_link() {
			local link=$1
			[[ $link == current ]] && echo sha-0123456789ab
			return 0
		}
		docker() { return 0; }
		clean_up
	)
}
check "clean_up succeeds after a first release" accept first_release_clean_up

# Regression: pt-backup's EXIT trap runs after restore_test has returned; it once read a local variable there, failed
# under set -u and left the throw-away container behind. pt-backup declares the same read-only names as pt-deploy-root,
# so it is sourced in a separate bash.
restore_test_trap_after_return() {
	local marker
	marker=$(mktemp)
	rm -f "$marker"
	# remove_scratch silences docker's output, so the stub leaves a file behind instead of printing.
	bash -c '
		set -euo pipefail
		source "$1"
		marker=$2
		docker() { if [[ $1 == rm && $3 == parking-tracker-restore-test-1 ]]; then touch "$marker"; fi; return 0; }
		start_scratch() { scratch=parking-tracker-restore-test-1; trap remove_scratch EXIT; }
		start_scratch
	' _ "$SERVER_DIR/pt-backup" "$marker"
	local status=$?
	[[ -f $marker ]] && rm -f "$marker" && ((status == 0))
}
check "pt-backup removes the restore-test container after the function returned" accept restore_test_trap_after_return

# provision.sh: the secrets of the sign-in are created once and never replaced by a rerun, an existing empty secret stops the
# script, a rotation moves the current key to the previous one and recreates the API container. The script is copied with its
# application folder pointed at a temporary one and the root check removed, so the functions run as the current user; chown
# records its calls instead of changing owners, docker records its arguments instead of running, and the current release is
# named by a function because a symbolic link needs rights that a developer PC may not give.
provision_copy() {
	local work=$1
	sed -e "s|^readonly APP_DIR=.*|readonly APP_DIR=$work/app|" -e 's|\[\[ \$EUID -eq 0 \]\]|true|' "$SERVER_DIR/provision.sh" >"$work/provision.sh"
}

provision_secrets_scenario() {
	local work status=0
	work=$(mktemp -d)
	provision_copy "$work"
	bash -c '
		set -euo pipefail
		source "$1/provision.sh"
		work=$1
		chown() { echo "$*" >>"$work/chown.log"; }
		docker() { echo "$*" >>"$work/docker.log"; }
		app=$APP_DIR/secrets/app
		mkdir -p "$app"
		create_token_secrets >/dev/null
		grep -q "BEGIN PRIVATE KEY" "$app/token_signing_key_current.pem"
		openssl pkey -in "$app/token_signing_key_current.pem" -noout -text | grep -q prime256v1
		[[ ! -s $app/token_signing_key_previous.pem ]]
		[[ $(base64 -d <"$app/subject_hash_key" | wc -c) -eq 32 ]]
		for name in token_signing_key_current.pem token_signing_key_previous.pem subject_hash_key; do
			grep -q "^1654:1654 $app/$name\." "$work/chown.log"
			case "$(uname -s)" in MINGW* | MSYS* | CYGWIN*) ;; *) [[ $(stat -c %a "$app/$name") == 400 ]] ;; esac
		done
		[[ -z $(ls -A "$app" | grep -v -e "^token_signing_key_" -e "^subject_hash_key$" || true) ]]
		before=$(cat "$app"/token_signing_key_current.pem "$app"/subject_hash_key | sha256sum)
		create_token_secrets >/dev/null
		[[ $before == "$(cat "$app"/token_signing_key_current.pem "$app"/subject_hash_key | sha256sum)" ]]
		[[ ! -s $app/token_signing_key_previous.pem ]]
		old_current=$(cat "$app/token_signing_key_current.pem")
		subject_key=$(cat "$app/subject_hash_key")
		release=$APP_DIR/releases/sha-0123456789ab
		mkdir -p "$release"
		current_release_dir() { echo "$release"; }
		rotate_signing_key >/dev/null
		[[ $(cat "$app/token_signing_key_previous.pem") == "$old_current" ]]
		[[ $(cat "$app/token_signing_key_current.pem") != "$old_current" ]]
		grep -q "BEGIN PRIVATE KEY" "$app/token_signing_key_current.pem"
		[[ $(cat "$app/subject_hash_key") == "$subject_key" ]]
		grep -qF -e "compose -p parking-tracker --project-directory $release -f $release/compose.yaml --env-file $release/.env up -d --force-recreate --no-deps api" "$work/docker.log"
		[[ -z $(ls -A "$app" | grep -v -e "^token_signing_key_" -e "^subject_hash_key$" || true) ]]
		create_token_secrets >/dev/null
		[[ $(cat "$app/token_signing_key_previous.pem") == "$old_current" ]]
	' _ "$work" || status=$?
	rm -rf "$work"
	return "$status"
}
check "provision.sh creates the sign-in secrets once with owner and mode, keeps them on a rerun, rotates the key and recreates the API" accept provision_secrets_scenario

provision_rotation_before_a_release() {
	local work status=0
	work=$(mktemp -d)
	provision_copy "$work"
	bash -c '
		set -euo pipefail
		source "$1/provision.sh"
		work=$1
		chown() { :; }
		docker() { echo "$*" >>"$work/docker.log"; }
		app=$APP_DIR/secrets/app
		mkdir -p "$app"
		create_token_secrets >/dev/null
		rotate_signing_key >/dev/null
		[[ ! -e $work/docker.log ]]
	' _ "$work" || status=$?
	rm -rf "$work"
	return "$status"
}
check "provision.sh rotates without touching Docker when no release is deployed yet" accept provision_rotation_before_a_release

provision_rotation_when_the_recreate_fails() {
	local work status=0
	work=$(mktemp -d)
	provision_copy "$work"
	bash -c '
		set -uo pipefail
		source "$1/provision.sh"
		chown() { :; }
		docker() { return 1; }
		app=$APP_DIR/secrets/app
		mkdir -p "$app"
		current_release_dir() { echo "$APP_DIR/releases/sha-0123456789ab"; }
		create_token_secrets >/dev/null
		rotate_signing_key
	' _ "$work" || status=$?
	rm -rf "$work"
	return "$status"
}
check "provision.sh fails loudly when the API cannot be recreated after a rotation" refuse provision_rotation_when_the_recreate_fails

provision_with_an_empty_secret() {
	local file=$1 work status=0
	work=$(mktemp -d)
	provision_copy "$work"
	bash -c '
		set -uo pipefail
		source "$1/provision.sh"
		chown() { :; }
		app=$APP_DIR/secrets/app
		mkdir -p "$app"
		create_token_secrets >/dev/null
		: >"$app/$2"
		create_token_secrets
	' _ "$work" "$file" || status=$?
	rm -rf "$work"
	return "$status"
}
check "provision.sh refuses an existing empty subject_hash_key" refuse provision_with_an_empty_secret subject_hash_key
check "provision.sh refuses an existing empty current signing key" refuse provision_with_an_empty_secret token_signing_key_current.pem

provision_rotation_without_a_key() {
	local work status=0
	work=$(mktemp -d)
	provision_copy "$work"
	bash -c '
		source "$1/provision.sh"
		mkdir -p "$APP_DIR/secrets/app"
		rotate_signing_key
	' _ "$work" || status=$?
	rm -rf "$work"
	return "$status"
}
check "provision.sh refuses to rotate when there is no current signing key" refuse provision_rotation_without_a_key

check "provision.sh refuses --rotate-signing-key with --deploy-key" refuse bash "$SERVER_DIR/provision.sh" --rotate-signing-key --deploy-key x

echo "pt-deploy tests: $passes passed, $failures failed"
((failures == 0))
