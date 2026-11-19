"""Tests of deploy/server/pt_compose_check.py (run: python -m unittest discover -s deploy/tests)."""

from __future__ import annotations

import copy
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "server"))

import pt_compose_check  # noqa: E402  (imported after the path is set)

DEPLOY_DIR = Path(__file__).resolve().parents[1]
SECRETS = "/opt/apps/parking-tracker/secrets"
ACCEPTED_SERVICE = {
    "image": "ghcr.io/annabigihed/parking-tracker-api@sha256:" + "0" * 64,
    "user": "1654:1654",
    "read_only": True,
    "cap_drop": ["ALL"],
    "security_opt": ["no-new-privileges:true"],
    "networks": {"data": None, "web": {"aliases": ["parking-api"]}},
    "volumes": [{"type": "volume", "source": "pgdata", "target": "/var/lib/postgresql/data", "volume": {}}],
    "tmpfs": ["/tmp"],
}
ACCEPTED_CONFIG = {
    "name": "parking-tracker",
    "services": {"api": ACCEPTED_SERVICE},
    "volumes": {"pgdata": {"name": "parking-tracker-pgdata"}},
    "networks": {
        "data": {"name": "parking-tracker_data", "ipam": {}, "internal": True},
        "web": {"name": "web", "ipam": {}, "external": True},
    },
    "secrets": {"api_connection": {"name": "parking-tracker_api_connection", "file": f"{SECRETS}/app/api_connection"}},
    "x-hardened": {"read_only": True},
}


def config_with(**service_changes: object) -> dict:
    """Return the accepted configuration with the api service changed."""
    config = copy.deepcopy(ACCEPTED_CONFIG)
    config["services"]["api"].update(service_changes)
    return config


def config_without(key: str) -> dict:
    """Return the accepted configuration with one key of the api service removed."""
    config = copy.deepcopy(ACCEPTED_CONFIG)
    del config["services"]["api"][key]
    return config


def refused(config: dict) -> bool:
    """Tell whether the check refuses a configuration."""
    return bool(pt_compose_check.config_findings(config))


class ServiceTests(unittest.TestCase):
    """What the check accepts and refuses in a service."""

    def test_the_repository_shape_is_accepted(self) -> None:
        self.assertEqual(pt_compose_check.config_findings(ACCEPTED_CONFIG), [])

    def test_any_key_outside_the_allow_list_is_refused(self) -> None:
        extra = {"privileged": True, "cap_add": ["SYS_ADMIN"], "ports": [{"target": 8080}], "devices": ["/dev/sda"],
                 "network_mode": "host", "pid": "host", "ipc": "host", "env_file": ["/etc/shadow"],
                 "configs": [{"source": "x"}], "volumes_from": ["caddy"], "extra_hosts": ["a:1.2.3.4"],
                 "sysctls": {"a": "1"}, "group_add": ["docker"], "build": {"context": "/"}, "extends": {"file": "/x"}}
        for key, value in extra.items():
            with self.subTest(key=key):
                self.assertTrue(refused(config_with(**{key: value})))

    def test_a_missing_or_root_user_is_refused(self) -> None:
        self.assertTrue(refused(config_without("user")))
        for user in ("root", "0", "0:0", ""):
            with self.subTest(user=user):
                self.assertTrue(refused(config_with(user=user)))

    def test_a_writable_root_file_system_is_refused(self) -> None:
        self.assertTrue(refused(config_with(read_only=False)))

    def test_cap_drop_must_be_exactly_all(self) -> None:
        self.assertTrue(refused(config_with(cap_drop=["NET_RAW"])))

    def test_security_opt_must_be_exactly_no_new_privileges(self) -> None:
        self.assertTrue(refused(config_with(security_opt=[])))
        self.assertTrue(refused(config_with(security_opt=["no-new-privileges:true", "seccomp=unconfined"])))

    def test_another_applications_network_is_refused(self) -> None:
        self.assertTrue(refused(config_with(networks={"raidmanager-prod_default": None})))

    def test_a_bind_mount_is_refused_wherever_it_points(self) -> None:
        for source in ("/", "/var/run/docker.sock", "/opt/apps/parking-tracker/secrets"):
            with self.subTest(source=source):
                volumes = [{"type": "bind", "source": source, "target": "/mnt"}]
                self.assertTrue(refused(config_with(volumes=volumes)))

    def test_a_volume_that_is_not_declared_by_the_project_is_refused(self) -> None:
        volumes = [{"type": "volume", "source": "raidmanager-prod_postgres-data", "target": "/mnt"}]
        self.assertTrue(refused(config_with(volumes=volumes)))


class TopLevelTests(unittest.TestCase):
    """What the check accepts and refuses at the top level."""

    def test_configs_and_unknown_keys_are_refused(self) -> None:
        for key in ("configs", "include", "models"):
            with self.subTest(key=key):
                config = copy.deepcopy(ACCEPTED_CONFIG)
                config[key] = {"x": {"file": "/etc/shadow"}}
                self.assertTrue(refused(config))

    def test_another_project_name_is_refused(self) -> None:
        config = copy.deepcopy(ACCEPTED_CONFIG)
        config["name"] = "raidmanager-prod"
        self.assertTrue(refused(config))

    def test_a_volume_with_a_driver_or_external_flag_is_refused(self) -> None:
        for volume in ({"name": "parking-tracker-pgdata", "driver_opts": {"type": "none", "o": "bind", "device": "/"}},
                       {"name": "parking-tracker-pgdata", "external": True},
                       {"name": "raidmanager-prod_postgres-data"}):
            with self.subTest(volume=volume):
                config = copy.deepcopy(ACCEPTED_CONFIG)
                config["volumes"]["pgdata"] = volume
                self.assertTrue(refused(config))

    def test_an_external_network_other_than_web_is_refused(self) -> None:
        config = copy.deepcopy(ACCEPTED_CONFIG)
        config["networks"]["data"] = {"name": "raidmanager-prod_default", "external": True}
        self.assertTrue(refused(config))

    def test_a_secret_file_outside_the_secrets_folder_is_refused(self) -> None:
        for source in ("/etc/shadow", "/opt/apps/parking-tracker/.docker/config.json",
                       "/opt/apps/parking-tracker/secrets/../.docker/config.json"):
            with self.subTest(source=source):
                config = copy.deepcopy(ACCEPTED_CONFIG)
                config["secrets"]["stolen"] = {"file": source}
                self.assertTrue(refused(config))


class RawTests(unittest.TestCase):
    """The check of the unrendered file."""

    def test_keys_that_read_other_files_are_found(self) -> None:
        for line in ("include:", "  extends:", "    env_file: /etc/shadow", "configs:", "    build: .",
                     "  - include: x"):
            with self.subTest(line=line):
                self.assertTrue(pt_compose_check.raw_findings(f"name: x\n{line}\n"))

    def test_the_repository_compose_file_has_none(self) -> None:
        self.assertEqual(pt_compose_check.raw_findings((DEPLOY_DIR / "compose.yaml").read_text(encoding="utf-8")), [])


class MainTests(unittest.TestCase):
    """Exit codes of the command."""

    def run_main(self, text: str, argv: list[str] | None = None) -> int:
        """Run main with text on standard input and return its exit code."""
        stdin = sys.stdin
        try:
            sys.stdin = io.StringIO(text)
            with redirect_stderr(io.StringIO()):
                return pt_compose_check.main(argv or [])
        finally:
            sys.stdin = stdin

    def test_an_accepted_configuration_exits_0(self) -> None:
        self.assertEqual(self.run_main(json.dumps(ACCEPTED_CONFIG)), 0)

    def test_a_refused_configuration_exits_1(self) -> None:
        self.assertEqual(self.run_main(json.dumps(config_with(privileged=True))), 1)

    def test_unreadable_input_exits_2(self) -> None:
        self.assertEqual(self.run_main("not json"), 2)
        self.assertEqual(self.run_main("[1, 2]"), 2)

    def test_the_raw_mode_exits_1_on_a_forbidden_key(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "compose.yaml"
            path.write_text("include:\n  - /etc/shadow\n", encoding="utf-8")
            self.assertEqual(self.run_main("", ["--raw", str(path)]), 1)


class RepositoryComposeFileTests(unittest.TestCase):
    """The repository's own compose.yaml, rendered by Docker Compose when it is available."""

    def test_the_repository_compose_file_is_accepted(self) -> None:
        if shutil.which("docker") is None:
            self.skipTest("docker is not installed")
        env = dict(os.environ, PT_SECRETS=SECRETS, API_IMAGE="api@sha256:" + "0" * 64,
                   MIGRATOR_IMAGE="migrator@sha256:" + "0" * 64)
        result = subprocess.run(
            ["docker", "compose", "-f", str(DEPLOY_DIR / "compose.yaml"), "--profile", "migrate", "config",
             "--format", "json", "--no-env-resolution"],
            capture_output=True, text=True, env=env, check=False)
        if result.returncode != 0:
            self.skipTest(f"docker compose config failed: {result.stderr.strip()}")
        config = json.loads(result.stdout)
        # On Windows, Compose prefixes absolute secret paths with the drive of the shell; the server never does.
        for secret in config.get("secrets", {}).values():
            secret["file"] = SECRETS + secret["file"].split(SECRETS, 1)[-1]
        self.assertEqual(pt_compose_check.config_findings(config), [])


if __name__ == "__main__":
    unittest.main()
