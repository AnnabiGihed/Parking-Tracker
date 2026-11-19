"""Refuse a Compose file that would give a Parking Tracker deployment more than it needs on the shared server.

pt-deploy-root runs this check on every release before starting it (ADR-0028). The Compose file comes from the
repository inside the migrator image, so anyone who can merge to main writes it, and root runs it. The check is an
allow-list: only the keys and values Parking Tracker uses pass; anything else is refused.

Usage:
  python3 pt_compose_check.py --raw compose.yaml   before rendering: refuse keys that make Compose read other files
                                                   (include, extends, env_file, configs, build)
  python3 pt_compose_check.py [rendered.json]      after `docker compose config --format json --no-env-resolution`,
                                                   from a file or standard input: the allow-list
Exit code 0 = accepted; 1 = refused, one line per finding on standard error; 2 = unreadable input.
Side effects: none; the script only reads its input.

provision.sh installs this file to /usr/local/sbin; a deployment never replaces it.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

PROJECT_NAME = "parking-tracker"
SECRETS_ROOT = "/opt/apps/parking-tracker/secrets/"
SHARED_NETWORK = "web"
ALLOWED_TOP_KEYS = {"name", "services", "secrets", "volumes", "networks"}
ALLOWED_SERVICE_KEYS = {
    "cap_drop", "command", "depends_on", "entrypoint", "environment", "healthcheck", "image", "mem_limit",
    "networks", "profiles", "read_only", "restart", "secrets", "security_opt", "tmpfs", "user", "volumes",
}
REQUIRED_SECURITY_OPTIONS = ["no-new-privileges:true"]
RAW_FORBIDDEN_KEYS = ("include", "extends", "env_file", "configs", "build")
RAW_KEY_PATTERN = re.compile(r"^\s*-?\s*(" + "|".join(RAW_FORBIDDEN_KEYS) + r")\s*:", re.MULTILINE)


def raw_findings(text: str) -> list[str]:
    """Return the forbidden keys found in the unrendered file, which Compose would resolve by reading other files."""
    return [f"key '{match.group(1)}' is not allowed" for match in RAW_KEY_PATTERN.finditer(text)]


def is_root_user(user: object) -> bool:
    """Tell whether a Compose `user` value is missing or names root."""
    name = str(user or "").split(":")[0].strip()
    return name in ("", "root", "0")


def service_findings(name: str, service: dict[str, Any], volume_names: set[str]) -> list[str]:
    """Return the reasons one service is refused; an empty list accepts it."""
    findings = [f"{name}: key '{key}' is not allowed" for key in sorted(set(service) - ALLOWED_SERVICE_KEYS)]
    if is_root_user(service.get("user")):
        findings.append(f"{name}: `user` must name a non-root user")
    if service.get("read_only") is not True:
        findings.append(f"{name}: read_only must be true")
    if [str(cap).upper() for cap in service.get("cap_drop") or []] != ["ALL"]:
        findings.append(f"{name}: cap_drop must be exactly [ALL]")
    options = [str(option).replace("=", ":") for option in service.get("security_opt") or []]
    if options != REQUIRED_SECURITY_OPTIONS:
        findings.append(f"{name}: security_opt must be exactly {REQUIRED_SECURITY_OPTIONS}")
    for network in service.get("networks") or {}:
        if network not in ("data", SHARED_NETWORK):
            findings.append(f"{name}: network '{network}' is not allowed")
    for volume in service.get("volumes") or []:
        if volume.get("type") != "volume" or volume.get("source") not in volume_names:
            findings.append(f"{name}: only the project's named volumes may be mounted ({volume.get('source')})")
    return findings


def volume_findings(volumes: dict[str, Any]) -> list[str]:
    """Return the reasons a top-level volume is refused: anything but a plain named volume of this project."""
    findings: list[str] = []
    for key, volume in volumes.items():
        volume = volume or {}
        if set(volume) - {"name"}:
            findings.append(f"volume {key}: only `name` is allowed ({sorted(set(volume) - {'name'})})")
        if not str(volume.get("name", "")).startswith(PROJECT_NAME + "-"):
            findings.append(f"volume {key}: the name must start with {PROJECT_NAME}-")
    return findings


def network_findings(networks: dict[str, Any]) -> list[str]:
    """Return the reasons a top-level network is refused: only `web` may be external, the others are internal."""
    findings: list[str] = []
    for key, network in networks.items():
        network = network or {}
        if key == SHARED_NETWORK:
            if network.get("external") is not True or network.get("name") != SHARED_NETWORK:
                findings.append(f"network {key}: must be the external network `{SHARED_NETWORK}`")
            continue
        extra = set(network) - {"name", "ipam", "internal"}
        if extra or network.get("internal") is not True or network.get("ipam") not in (None, {}):
            findings.append(f"network {key}: must be a plain internal network of the project")
    return findings


def secret_findings(secrets: dict[str, Any]) -> list[str]:
    """Return the reasons a top-level secret is refused: a file outside the project's secrets folder."""
    findings: list[str] = []
    for key, secret in secrets.items():
        secret = secret or {}
        source = str(secret.get("file", ""))
        if set(secret) - {"name", "file"} or not source.startswith(SECRETS_ROOT) or ".." in Path(source).parts:
            findings.append(f"secret {key}: only a file under {SECRETS_ROOT} is allowed ({source})")
    return findings


def config_findings(config: dict[str, Any]) -> list[str]:
    """Return every finding of a rendered Compose configuration."""
    findings = [f"top-level key '{key}' is not allowed"
                for key in sorted(set(config) - ALLOWED_TOP_KEYS) if not key.startswith("x-")]
    if config.get("name") != PROJECT_NAME:
        findings.append(f"the project name must be {PROJECT_NAME}")
    volumes = config.get("volumes") or {}
    for name, service in (config.get("services") or {}).items():
        findings.extend(service_findings(name, service, set(volumes)))
    findings.extend(volume_findings(volumes))
    findings.extend(network_findings(config.get("networks") or {}))
    findings.extend(secret_findings(config.get("secrets") or {}))
    return findings


def report(findings: list[str]) -> int:
    """Print the findings to standard error and return the exit code."""
    for finding in findings:
        print(f"pt-compose-check: refused: {finding}", file=sys.stderr)
    return 1 if findings else 0


def main(argv: list[str] | None = None) -> int:
    """Run the raw check or the rendered check, as the arguments ask."""
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--raw", metavar="COMPOSE_FILE", help="check the unrendered compose.yaml for forbidden keys")
    parser.add_argument("rendered", nargs="?", help="output of `docker compose config --format json`; stdin if omitted")
    args = parser.parse_args(argv)
    try:
        if args.raw:
            return report(raw_findings(Path(args.raw).read_text(encoding="utf-8")))
        text = Path(args.rendered).read_text(encoding="utf-8") if args.rendered else sys.stdin.read()
        config = json.loads(text)
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        print(f"pt-compose-check: cannot read the configuration: {error}", file=sys.stderr)
        return 2
    if not isinstance(config, dict):
        print("pt-compose-check: the configuration is not an object", file=sys.stderr)
        return 2
    return report(config_findings(config))


if __name__ == "__main__":
    sys.exit(main())
