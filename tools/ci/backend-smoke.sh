#!/usr/bin/env bash
# Smoke test of the back-end images with deploy/compose.yaml, as backend.yml's smoke job runs it (ADR-0023).
#
# Usage:
#   backend-smoke.sh prepare <secrets dir>   throw-away passwords and connection strings, owned like on the server
#                                            (999 for postgres, 1654 for the .NET images), and the `web` network
#   backend-smoke.sh start                   PostgreSQL, deploy/sql/roles.sql, the migrator, then the API; needs
#                                            PT_SECRETS, API_IMAGE and MIGRATOR_IMAGE in the environment
#   backend-smoke.sh check <curl image>      from a container on `web`, as the shared Caddy reaches the API:
#                                            /health/live and /health/ready 200, /scalar and /swagger 404
# Exit code 0 on success; 1 with an ::error:: line otherwise. Uses sudo for chown, as GitHub's runners allow.
set -euo pipefail

readonly COMPOSE_FILE=deploy/compose.yaml
readonly POSTGRES_UID=999
readonly DOTNET_APP_UID=1654

error() { echo "::error::$*" >&2; exit 1; }

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

write_secret() {
	local path=$1 owner=$2 value=$3
	printf '%s' "$value" >"$path"
	sudo chown "$owner:$owner" "$path"
	sudo chmod 400 "$path"
}

prepare() {
	local dir=$1 owner_password api_password
	install -d -m 755 "$dir/postgres" "$dir/app"
	owner_password=$(openssl rand -hex 16)
	api_password=$(openssl rand -hex 16)
	write_secret "$dir/postgres/postgres_password" "$POSTGRES_UID" "$(openssl rand -hex 16)"
	write_secret "$dir/postgres/pt_owner_password" "$POSTGRES_UID" "$owner_password"
	write_secret "$dir/postgres/pt_api_password" "$POSTGRES_UID" "$api_password"
	write_secret "$dir/app/migrator_connection" "$DOTNET_APP_UID" \
		"Host=postgres;Port=5432;Database=parkingdata;Username=pt_owner;Password=$owner_password;Maximum Pool Size=2"
	write_secret "$dir/app/api_connection" "$DOTNET_APP_UID" \
		"Host=postgres;Port=5432;Database=parkingdata;Username=pt_api;Password=$api_password;Maximum Pool Size=10"
	docker network inspect web >/dev/null 2>&1 || docker network create web >/dev/null
}

start() {
	: "${PT_SECRETS:?}" "${API_IMAGE:?}" "${MIGRATOR_IMAGE:?}"
	compose up -d --wait postgres || error "PostgreSQL did not become healthy"
	compose exec -T postgres psql -v ON_ERROR_STOP=1 -U postgres -d postgres -f - <deploy/sql/roles.sql >/dev/null ||
		error "deploy/sql/roles.sql failed"
	compose --profile migrate run --rm migrator || error "the migrator failed"
	# The roles step runs again after the migration, as on the second deployment, to prove it stays idempotent.
	compose exec -T postgres psql -v ON_ERROR_STOP=1 -U postgres -d postgres -f - <deploy/sql/roles.sql >/dev/null ||
		error "deploy/sql/roles.sql failed on its second run"
	compose up -d --no-deps api || error "the API did not start"
}

expect_status() {
	local curl_image=$1 path=$2 expected=$3 status="" tries=0
	# The API answers inside the Docker network without TLS, as the shared Caddy reaches it in production (TLS ends at
	# Caddy, ADR-0028); curl uses that protocol for an address without a scheme.
	while ((tries < 30)); do
		status=$(docker run --rm --network web "$curl_image" -s -o /dev/null -w '%{response_code}' --max-time 5 \
			"parking-api:8080$path" || true)
		[[ $status == "$expected" ]] && { echo "$path -> $status"; return 0; }
		tries=$((tries + 1))
		sleep 2
	done
	error "$path answered $status, expected $expected"
}

check() {
	local curl_image=$1
	expect_status "$curl_image" /health/live 200
	expect_status "$curl_image" /health/ready 200
	expect_status "$curl_image" /scalar 404
	expect_status "$curl_image" /swagger/v1/swagger.json 404
}

case "${1:-}" in
	prepare) prepare "${2:?usage: backend-smoke.sh prepare <secrets dir>}" ;;
	start) start ;;
	check) check "${2:?usage: backend-smoke.sh check <curl image>}" ;;
	*) echo "usage: backend-smoke.sh prepare <dir> | start | check <curl image>" >&2; exit 2 ;;
esac
