#!/usr/bin/env bash
# Prepares the shared OVH server for Parking Tracker (ADR-0028). The owner runs it once, and again after a change to
# this folder, from a copy of deploy/ on the server:
#
#   sudo bash deploy/server/provision.sh --deploy-key parking-deploy.pub
#   sudo bash deploy/server/provision.sh --rotate-signing-key
#
# It only adds; it never reinstalls, resets the firewall, changes sshd or touches another application:
#   1. checks the server: Docker, Python 3, the shared `caddy` container, its Caddyfile and the `web` network;
#   2. creates the `parking-deploy` account (not in the docker group) with the deploy key restricted to pt-deploy, and
#      reports whether sshd's AllowUsers or AllowGroups admit it;
#   3. installs pt-deploy, pt-deploy-root, pt-backup, pt-erasure-reapply and pt_compose_check.py, and the single
#      sudoers line;
#   4. creates /opt/apps/parking-tracker and the secret files: database passwords generated here (they never leave
#      the server), the proxy key, the token signing keys and the subject hash key (each generated once, never rotated by a
#      rerun) and the GHCR read token, typed at a prompt;
#   5. writes the Parking Tracker block into the shared Caddyfile between its marker lines, with the proxy key and the
#      privacy notice (deploy/site/privacy-notice.html) in place of their placeholders (pt_caddy_block.py), validates
#      and reloads Caddy, and restores the previous file when Caddy rejects it;
#   6. installs and starts the nightly dump and monthly restore-test timers.
# --rotate-signing-key does only one thing: the current token signing key becomes the previous one (still accepted, so
# no one is signed out), a new current key is made, and the API container of the current release is recreated. The previous
# key of before is dropped, so rotate again only after the access tokens of the last rotation have expired (15 minutes).
# The subject hash key is never rotated: a new one would orphan every account and the erasure log.
# Exit code 0 when everything is in place; 1 with a message otherwise. Safe to run again.
set -euo pipefail

readonly APP_DIR=/opt/apps/parking-tracker
readonly SECRETS_DIR=$APP_DIR/secrets
readonly DEPLOY_USER=parking-deploy
readonly PROXY_CONTAINER=caddy
readonly PROXY_CADDYFILE=/opt/apps/proxy/Caddyfile
readonly PROXY_CONFIG_IN_CONTAINER=/etc/caddy/Caddyfile
readonly BEGIN_MARKER="# BEGIN Parking Tracker (ADR-0028)"
readonly END_MARKER="# END Parking Tracker (ADR-0028)"
readonly SUDOERS_FILE=/etc/sudoers.d/parking-tracker
readonly POSTGRES_UID=999
readonly DOTNET_APP_UID=1654
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
readonly SCRIPT_DIR
readonly DEPLOY_DIR=$SCRIPT_DIR/..

# The Caddyfile candidate holds the proxy key, so every way out of the script removes it.
caddy_candidate=""
trap 'rm -f -- "$caddy_candidate"' EXIT

step() { printf '\n== %s\n' "$*"; }
fail() { echo "provision: $*" >&2; exit 1; }

usage() {
	echo "usage: sudo bash provision.sh --deploy-key <public key file> | --rotate-signing-key" >&2
	exit 1
}

check_server() {
	step "Server checks"
	[[ $EUID -eq 0 ]] || fail "run with sudo"
	command -v docker >/dev/null || fail "Docker is not installed"
	command -v python3 >/dev/null || fail "python3 is not installed"
	command -v flock >/dev/null || fail "flock is not installed"
	docker inspect "$PROXY_CONTAINER" >/dev/null 2>&1 || fail "the shared proxy container '$PROXY_CONTAINER' is not running"
	[[ -f $PROXY_CADDYFILE ]] || fail "the shared Caddyfile is not at $PROXY_CADDYFILE"
	docker network inspect web >/dev/null 2>&1 || fail "the shared Docker network 'web' does not exist"
	echo "Docker, python3, flock, the shared Caddy and the web network are there"
}

create_deploy_account() {
	local key_file=$1
	step "Account $DEPLOY_USER"
	[[ -s $key_file ]] || fail "the public key file $key_file is empty or missing"
	local key
	key=$(head -n 1 "$key_file")
	[[ $key =~ ^(ssh-ed25519|ecdsa-sha2-nistp256|ssh-rsa)\ [A-Za-z0-9+/=]+(\ .*)?$ ]] || fail "$key_file is not an SSH public key"
	id "$DEPLOY_USER" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$DEPLOY_USER"
	passwd -l "$DEPLOY_USER" >/dev/null
	local ssh_dir
	ssh_dir=$(getent passwd "$DEPLOY_USER" | cut -d: -f6)/.ssh
	install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$ssh_dir"
	printf 'restrict,command="/usr/local/bin/pt-deploy" %s\n' "$key" >"$ssh_dir/authorized_keys"
	chown "$DEPLOY_USER:$DEPLOY_USER" "$ssh_dir/authorized_keys"
	chmod 600 "$ssh_dir/authorized_keys"
	if id -nG "$DEPLOY_USER" | tr ' ' '\n' | grep -qx docker; then
		fail "$DEPLOY_USER is in the docker group; remove it (it must reach Docker only through pt-deploy-root)"
	fi
	local allowed
	allowed=$(sshd -T -C "user=$DEPLOY_USER,host=localhost,addr=127.0.0.1" 2>/dev/null | grep -Ei '^(allowusers|allowgroups) ' || true)
	if [[ -n $allowed ]]; then
		echo "sshd limits sign-in ($allowed); make sure it admits $DEPLOY_USER (this script does not change sshd)"
	else
		echo "sshd has no AllowUsers or AllowGroups; $DEPLOY_USER can sign in with its key"
	fi
}

install_programs() {
	step "Programs and sudoers"
	install -m 755 -o root -g root "$SCRIPT_DIR/pt-deploy" /usr/local/bin/pt-deploy
	install -m 700 -o root -g root "$SCRIPT_DIR/pt-deploy-root" /usr/local/sbin/pt-deploy-root
	install -m 700 -o root -g root "$SCRIPT_DIR/pt-backup" /usr/local/sbin/pt-backup
	install -m 700 -o root -g root "$SCRIPT_DIR/pt-erasure-reapply" /usr/local/sbin/pt-erasure-reapply
	install -m 644 -o root -g root "$SCRIPT_DIR/pt_compose_check.py" /usr/local/sbin/pt_compose_check.py
	local candidate
	candidate=$(mktemp)
	printf '%s ALL=(root) NOPASSWD: /usr/local/sbin/pt-deploy-root\n' "$DEPLOY_USER" >"$candidate"
	visudo -cf "$candidate" >/dev/null || { rm -f "$candidate"; fail "visudo rejected the sudoers line"; }
	install -m 440 -o root -g root "$candidate" "$SUDOERS_FILE"
	rm -f "$candidate"
	echo "Installed pt-deploy, pt-deploy-root, pt-backup, pt-erasure-reapply, pt_compose_check.py and $SUDOERS_FILE"
}

write_secret() {
	local path=$1 owner=$2 value=$3 temp
	umask 077
	# The new content is made complete beside the target, with its owner and mode 0400, and moved over it in one step, so a
	# reader never meets a missing or half-written secret.
	temp=$(mktemp "$path.XXXXXX") || fail "could not create a file beside $path"
	if ! { printf '%s' "$value" >"$temp" && chown "$owner:$owner" "$temp" && chmod 400 "$temp" && mv -f -- "$temp" "$path"; }; then
		rm -f -- "$temp"
		fail "could not write $path"
	fi
}

# Prints a new P-256 private key in PEM, the key type of the ES256 access tokens (ADR-0014).
new_signing_key() {
	openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256
}

# Creates a secret of the sign-in when its file is missing. A file that exists but is empty is never replaced: it means that
# the secret was lost or damaged, and a new value would silently orphan the accounts or sign everyone out.
# Usage: create_secret_once <path> <label> <generator command and arguments>
create_secret_once() {
	local path=$1 label=$2 value
	shift 2
	if [[ -e $path ]]; then
		[[ -s $path ]] || fail "$path exists but is empty; restore $label from its copy (this script never replaces it)"
		return 0
	fi
	value=$("$@") || fail "could not generate $label"
	[[ -n $value ]] || fail "generating $label gave nothing"
	write_secret "$path" "$DOTNET_APP_UID" "$value"
	echo "Generated $label"
}

# The secrets of the sign-in, each created only when missing, so a rerun never invalidates a token or an account:
# the current signing key, the previous one (an empty file until the first rotation) and the subject hash key.
create_token_secrets() {
	local app=$SECRETS_DIR/app
	create_secret_once "$app/token_signing_key_current.pem" "the current token signing key" new_signing_key
	if [[ ! -e $app/token_signing_key_previous.pem ]]; then
		write_secret "$app/token_signing_key_previous.pem" "$DOTNET_APP_UID" ""
		echo "Created the empty token_signing_key_previous.pem"
	fi
	create_secret_once "$app/subject_hash_key" "the subject hash key (never rotated; keep a copy in the password manager)" openssl rand -base64 32
}

# Prints the folder of the release the "current" link points at, or nothing before the first deployment.
current_release_dir() {
	local link=$APP_DIR/current
	if [[ -L $link ]]; then
		readlink -f "$link"
	fi
}

# Recreates the API container of the current release, so that it reads the files that changed. The invocation is the one
# of pt-deploy-root; before the first deployment there is nothing to recreate.
recreate_api() {
	local release_dir
	release_dir=$(current_release_dir)
	if [[ -z $release_dir ]]; then
		echo "No release is deployed yet; the first deployment reads the new keys."
		return 0
	fi
	docker compose -p parking-tracker --project-directory "$release_dir" -f "$release_dir/compose.yaml" \
		--env-file "$release_dir/.env" up -d --force-recreate --no-deps api ||
		fail "the API container could not be recreated; the keys are rotated, so deploy the current tag or restart the API"
}

# Makes the current signing key the previous one and creates a new current key, then recreates the API container so that
# it reads them. The new key is generated before anything is replaced, so a failure leaves both files as they were.
rotate_signing_key() {
	step "Rotate the token signing key"
	[[ $EUID -eq 0 ]] || fail "run with sudo"
	local app=$SECRETS_DIR/app new_key old_key
	[[ -s $app/token_signing_key_current.pem ]] || fail "there is no current signing key to rotate; run the full provisioning first"
	new_key=$(new_signing_key) || fail "openssl could not make a key"
	[[ -n $new_key ]] || fail "openssl made no key"
	old_key=$(cat "$app/token_signing_key_current.pem") || fail "could not read the current key"
	write_secret "$app/token_signing_key_previous.pem" "$DOTNET_APP_UID" "$old_key"
	write_secret "$app/token_signing_key_current.pem" "$DOTNET_APP_UID" "$new_key"
	echo "Rotated: the old current key is now the previous one, and a new current key is in place."
	recreate_api
	echo "The API reads the new keys; access tokens signed with the old key stay valid for 15 minutes."
}

create_folders_and_secrets() {
	step "Folders and secrets"
	install -d -m 711 -o root -g root "$APP_DIR"
	install -d -m 700 -o root -g root "$APP_DIR/releases" "$APP_DIR/backups" "$APP_DIR/backups/pre-deploy" \
		"$APP_DIR/.docker" "$SECRETS_DIR" "$SECRETS_DIR/postgres" "$SECRETS_DIR/app"
	local password name
	for name in postgres_password pt_owner_password pt_api_password; do
		if [[ ! -s $SECRETS_DIR/postgres/$name ]]; then
			password=$(openssl rand -hex 32)
			write_secret "$SECRETS_DIR/postgres/$name" "$POSTGRES_UID" "$password"
			echo "Generated $name"
		fi
	done
	local owner_password api_password
	owner_password=$(cat "$SECRETS_DIR/postgres/pt_owner_password")
	api_password=$(cat "$SECRETS_DIR/postgres/pt_api_password")
	write_secret "$SECRETS_DIR/app/migrator_connection" "$DOTNET_APP_UID" \
		"Host=postgres;Port=5432;Database=parkingdata;Username=pt_owner;Password=$owner_password;Maximum Pool Size=2"
	write_secret "$SECRETS_DIR/app/api_connection" "$DOTNET_APP_UID" \
		"Host=postgres;Port=5432;Database=parkingdata;Username=pt_api;Password=$api_password;Maximum Pool Size=10"
	if [[ ! -s $SECRETS_DIR/app/proxy_key ]]; then
		write_secret "$SECRETS_DIR/app/proxy_key" "$DOTNET_APP_UID" "$(openssl rand -hex 32)"
		echo "Generated proxy_key"
	fi
	create_token_secrets
	if [[ ! -s $SECRETS_DIR/ghcr_token ]]; then
		local token
		read -r -s -p "GitHub token with read:packages only (input hidden): " token
		echo
		[[ -n $token ]] || fail "no token given"
		write_secret "$SECRETS_DIR/ghcr_token" root "$token"
		echo "Stored the GHCR read token"
	fi
	echo "Secrets in $SECRETS_DIR (database passwords generated on this server, never copied elsewhere)"
}

# Prints the Caddyfile with the Parking Tracker block replaced, or appended when the markers are absent, with the proxy
# key and the privacy notice page in place of their placeholders. The proxy key reaches the script through the
# environment, not the argument list, so that no process listing shows it. The script reads parking.Caddyfile and
# site/privacy-notice.html from its own deploy/ folder and the current Caddyfile from standard input.
caddyfile_with_block() {
	local current=$1
	PT_PROXY_KEY=$(cat "$SECRETS_DIR/app/proxy_key") python3 "$SCRIPT_DIR/pt_caddy_block.py" \
		"$BEGIN_MARKER" "$END_MARKER" <"$current"
}

# Has the shared Caddy check the candidate before the live file is touched; the copy in the container is removed again.
validate_candidate() {
	local candidate=$1 in_container=/tmp/parking-tracker-candidate.Caddyfile status=0
	docker cp "$candidate" "$PROXY_CONTAINER:$in_container" || fail "could not copy the candidate into $PROXY_CONTAINER"
	docker exec "$PROXY_CONTAINER" caddy validate --config "$in_container" --adapter caddyfile || status=$?
	docker exec "$PROXY_CONTAINER" rm -f "$in_container" || true
	((status == 0)) || fail "Caddy rejected the Parking Tracker block; the shared Caddyfile is untouched"
}

install_caddy_sites() {
	step "Caddy sites"
	local block=$DEPLOY_DIR/parking.Caddyfile page=$DEPLOY_DIR/site/privacy-notice.html backup candidate
	[[ -f $block ]] || fail "parking.Caddyfile is missing next to this folder"
	[[ -f $page ]] || fail "site/privacy-notice.html is missing next to this folder (copy the whole deploy/ folder)"
	candidate=$(mktemp)
	caddy_candidate=$candidate
	caddyfile_with_block "$PROXY_CADDYFILE" >"$candidate" || fail "could not build the Caddyfile"
	if cmp -s "$candidate" "$PROXY_CADDYFILE"; then
		rm -f "$candidate"
		echo "The shared Caddyfile already has the current block"
		return
	fi
	validate_candidate "$candidate"
	# The backup is ours and may hold the key; the live file belongs to another project and keeps its mode.
	backup="$PROXY_CADDYFILE.$(date +%Y%m%d%H%M%S).bak"
	(umask 077 && cp "$PROXY_CADDYFILE" "$backup")
	# Written in place: the proxy bind-mounts the single file, and a new inode would stay invisible to it.
	cat "$candidate" >"$PROXY_CADDYFILE"
	rm -f "$candidate"
	if ! docker exec "$PROXY_CONTAINER" caddy reload --config "$PROXY_CONFIG_IN_CONTAINER" --adapter caddyfile; then
		cat "$backup" >"$PROXY_CADDYFILE"
		docker exec "$PROXY_CONTAINER" caddy reload --config "$PROXY_CONFIG_IN_CONTAINER" --adapter caddyfile || true
		fail "Caddy did not reload; the Caddyfile is restored from $backup"
	fi
	echo "Block written and Caddy reloaded; the previous Caddyfile is $backup"
}

install_timers() {
	step "Backup timers"
	local unit
	for unit in "$SCRIPT_DIR"/systemd/*.service "$SCRIPT_DIR"/systemd/*.timer; do
		install -m 644 -o root -g root "$unit" "/etc/systemd/system/$(basename "$unit")"
	done
	systemctl daemon-reload
	systemctl enable --now parking-tracker-dump.timer parking-tracker-restore-test.timer >/dev/null
	systemctl list-timers 'parking-tracker-*' --no-pager
}

main() {
	local key_file="" rotate=false
	while (($#)); do
		case "$1" in
			--deploy-key) key_file=${2:-}; shift 2 || usage ;;
			--rotate-signing-key) rotate=true; shift ;;
			*) usage ;;
		esac
	done
	if [[ $rotate == true ]]; then
		[[ -z $key_file ]] || usage
		rotate_signing_key
		return
	fi
	[[ -n $key_file ]] || usage
	check_server
	create_deploy_account "$key_file"
	install_programs
	create_folders_and_secrets
	install_caddy_sites
	install_timers
	step "Done"
	echo "Next: the backend.yml deployment from main (docs/operations/runbook.md)"
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
	main "$@"
fi
