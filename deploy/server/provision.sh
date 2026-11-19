#!/usr/bin/env bash
# Prepares the shared OVH server for Parking Tracker (ADR-0028). The owner runs it once, and again after a change to
# this folder, from a copy of deploy/ on the server:
#
#   sudo bash deploy/server/provision.sh --deploy-key parking-deploy.pub
#
# It only adds; it never reinstalls, resets the firewall, changes sshd or touches another application:
#   1. checks the server: Docker, Python 3, the shared `caddy` container, its Caddyfile and the `web` network;
#   2. creates the `parking-deploy` account (not in the docker group) with the deploy key restricted to pt-deploy, and
#      reports whether sshd's AllowUsers or AllowGroups admit it;
#   3. installs pt-deploy, pt-deploy-root, pt-backup and pt_compose_check.py, and the single sudoers line;
#   4. creates /opt/apps/parking-tracker and the secret files: database passwords generated here (they never leave
#      the server) and the GHCR read token, typed at a prompt;
#   5. writes the Parking Tracker block into the shared Caddyfile between its marker lines, validates and reloads
#      Caddy, and restores the previous file when Caddy rejects it;
#   6. installs and starts the nightly dump and monthly restore-test timers.
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

step() { printf '\n== %s\n' "$*"; }
fail() { echo "provision: $*" >&2; exit 1; }

usage() {
	echo "usage: sudo bash provision.sh --deploy-key <public key file>" >&2
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
	install -m 644 -o root -g root "$SCRIPT_DIR/pt_compose_check.py" /usr/local/sbin/pt_compose_check.py
	local candidate
	candidate=$(mktemp)
	printf '%s ALL=(root) NOPASSWD: /usr/local/sbin/pt-deploy-root\n' "$DEPLOY_USER" >"$candidate"
	visudo -cf "$candidate" >/dev/null || { rm -f "$candidate"; fail "visudo rejected the sudoers line"; }
	install -m 440 -o root -g root "$candidate" "$SUDOERS_FILE"
	rm -f "$candidate"
	echo "Installed pt-deploy, pt-deploy-root, pt-backup, pt_compose_check.py and $SUDOERS_FILE"
}

write_secret() {
	local path=$1 owner=$2 value=$3
	umask 077
	printf '%s' "$value" >"$path"
	chown "$owner:$owner" "$path"
	chmod 400 "$path"
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

# Prints the Caddyfile with the Parking Tracker block replaced, or appended when the markers are absent.
caddyfile_with_block() {
	local current=$1 block=$2
	python3 - "$current" "$block" "$BEGIN_MARKER" "$END_MARKER" <<'PYTHON'
import sys
from pathlib import Path

current, block, begin, end = sys.argv[1:5]
text = Path(current).read_text(encoding="utf-8")
new_block = Path(block).read_text(encoding="utf-8").rstrip("\n") + "\n"
if begin in text and end in text:
    head, rest = text.split(begin, 1)
    _, tail = rest.split(end, 1)
    text = head + new_block + tail.lstrip("\n")
else:
    text = text.rstrip("\n") + "\n\n" + new_block
sys.stdout.write(text)
PYTHON
}

install_caddy_sites() {
	step "Caddy sites"
	local block=$DEPLOY_DIR/parking.Caddyfile backup candidate
	[[ -f $block ]] || fail "parking.Caddyfile is missing next to this folder"
	candidate=$(mktemp)
	caddyfile_with_block "$PROXY_CADDYFILE" "$block" >"$candidate"
	if cmp -s "$candidate" "$PROXY_CADDYFILE"; then
		rm -f "$candidate"
		echo "The shared Caddyfile already has the current block"
		return
	fi
	backup="$PROXY_CADDYFILE.$(date +%Y%m%d%H%M%S).bak"
	cp -p "$PROXY_CADDYFILE" "$backup"
	# Written in place: the proxy bind-mounts the single file, and a new inode would stay invisible to it.
	cat "$candidate" >"$PROXY_CADDYFILE"
	rm -f "$candidate"
	if ! docker exec "$PROXY_CONTAINER" caddy validate --config "$PROXY_CONFIG_IN_CONTAINER" --adapter caddyfile; then
		cat "$backup" >"$PROXY_CADDYFILE"
		fail "Caddy rejected the Parking Tracker block; the Caddyfile is restored from $backup"
	fi
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
	local key_file=""
	while (($#)); do
		case "$1" in
			--deploy-key) key_file=${2:-}; shift 2 || usage ;;
			*) usage ;;
		esac
	done
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
