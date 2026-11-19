"""Tests of deploy/server/pt_caddy_block.py and of the privacy notice the shared Caddy serves.

Run: python -m unittest discover -s deploy/tests -v
The Docker test starts the caddy:2 image and runs only with PT_CADDY_DOCKER=1 (it needs Docker and the image).
"""

from __future__ import annotations

import http.client
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

DEPLOY_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DEPLOY_DIR / "server"))

import pt_caddy_block  # noqa: E402  (imported after the path is set)

BEGIN = "# BEGIN Parking Tracker (ADR-0028)"
END = "# END Parking Tracker (ADR-0028)"
BLOCK_FILE = DEPLOY_DIR / "parking.Caddyfile"
PAGE_FILE = DEPLOY_DIR / "site" / "privacy-notice.html"
CADDY_IMAGE = "caddy:2"
LOCAL_PORT = 18080


class RenderBlockTests(unittest.TestCase):
    """The substitution that provision.sh runs."""

    def test_committed_block_has_one_page_placeholder_and_the_page_exists(self) -> None:
        self.assertEqual(1, len(pt_caddy_block.PAGE_LINE.findall(BLOCK_FILE.read_text(encoding="utf-8"))))
        self.assertTrue(PAGE_FILE.is_file())

    def test_page_and_key_replace_their_placeholders(self) -> None:
        rendered = pt_caddy_block.render_block(
            BLOCK_FILE.read_text(encoding="utf-8"), PAGE_FILE.read_text(encoding="utf-8"), "secret-key"
        )
        self.assertIsNone(pt_caddy_block.PAGE_LINE.search(rendered))
        self.assertNotIn("header_up X-PT-Proxy-Key " + pt_caddy_block.PROXY_KEY_PLACEHOLDER, rendered)
        self.assertIn("header_up X-PT-Proxy-Key secret-key", rendered)
        self.assertIn("<title>Parking Tracker: privacy notice</title>", rendered)

    def test_braces_are_escaped_so_caddy_does_not_expand_them(self) -> None:
        self.assertEqual("a \\{b\\} c", pt_caddy_block.escape_page("a {b} c\n"))

    def test_page_with_the_heredoc_marker_is_refused(self) -> None:
        with self.assertRaises(pt_caddy_block.BlockError):
            pt_caddy_block.escape_page(f"<p>{pt_caddy_block.HEREDOC_MARKER}</p>")

    def test_page_with_a_backslash_is_refused(self) -> None:
        with self.assertRaises(pt_caddy_block.BlockError):
            pt_caddy_block.escape_page("<p>a\\b</p>")

    def test_block_without_the_page_placeholder_is_refused(self) -> None:
        with self.assertRaises(pt_caddy_block.BlockError):
            pt_caddy_block.render_block("site {\n}\n", "<p></p>", "key")

    def test_block_replaces_the_marked_block_and_keeps_the_rest(self) -> None:
        current = f"first {{\n}}\n\n{BEGIN}\nold\n{END}\n\nlast {{\n}}\n"
        merged = pt_caddy_block.merge_block(current, f"{BEGIN}\nnew\n{END}\n", BEGIN, END)
        self.assertEqual(f"first {{\n}}\n\n{BEGIN}\nnew\n{END}\nlast {{\n}}\n", merged)

    def test_key_occurs_exactly_once_in_the_rendered_block(self) -> None:
        rendered = pt_caddy_block.render_block(
            BLOCK_FILE.read_text(encoding="utf-8"), PAGE_FILE.read_text(encoding="utf-8"), "secret-key"
        )
        self.assertEqual(1, rendered.count("secret-key"))

    def test_rendering_twice_gives_the_same_caddyfile(self) -> None:
        page = PAGE_FILE.read_text(encoding="utf-8")
        first = self.run_cli(page=page, current=f"a {{\n}}\n\n{BEGIN}\nold\n{END}\n")
        second = self.run_cli(page=page, current=first.stdout)
        self.assertEqual(0, second.returncode)
        self.assertEqual(first.stdout, second.stdout)

    def test_command_line_prints_the_expected_caddyfile(self) -> None:
        page = PAGE_FILE.read_text(encoding="utf-8")
        result = self.run_cli(page=page, current="first {\n}\n")
        block = pt_caddy_block.render_block(BLOCK_FILE.read_text(encoding="utf-8"), page, "key-1")
        self.assertEqual(0, result.returncode)
        self.assertEqual("first {\n}\n\n" + block, result.stdout)

    def test_command_line_refuses_what_it_cannot_write_safely(self) -> None:
        page = PAGE_FILE.read_text(encoding="utf-8")
        cases = {
            "no key": {"page": page, "key": None},
            "empty key": {"page": page, "key": ""},
            "key with a space": {"page": page, "key": "a b"},
            "key with a brace": {"page": page, "key": "a{b"},
            "page with the heredoc marker": {"page": f"<p>{pt_caddy_block.HEREDOC_MARKER}</p>"},
            "page with the key placeholder": {"page": f"<p>{pt_caddy_block.PROXY_KEY_PLACEHOLDER}</p>"},
            "page missing": {"page": None},
            "begin without end": {"page": page, "current": f"{BEGIN}\nold\n"},
            "end before begin": {"page": page, "current": f"{END}\nold\n{BEGIN}\n"},
        }
        for name, arguments in cases.items():
            with self.subTest(name):
                result = self.run_cli(**arguments)
                self.assertEqual(1, result.returncode)
                self.assertEqual("", result.stdout)
                self.assertIn("pt_caddy_block:", result.stderr)

    @staticmethod
    def run_cli(page: str | None, current: str = "", key: str | None = "key-1") -> subprocess.CompletedProcess[str]:
        """Runs the script on temporary files; a page of None leaves the page file out."""
        with tempfile.TemporaryDirectory() as folder:
            current_file = Path(folder) / "Caddyfile"
            current_file.write_text(current, encoding="utf-8", newline="\n")
            page_file = Path(folder) / "page.html"
            if page is not None:
                page_file.write_text(page, encoding="utf-8", newline="\n")
            environment = {name: value for name, value in os.environ.items() if name != "PT_PROXY_KEY"}
            if key is not None:
                environment["PT_PROXY_KEY"] = key
            return subprocess.run(
                [sys.executable, str(DEPLOY_DIR / "server" / "pt_caddy_block.py"), str(current_file), str(BLOCK_FILE),
                 str(page_file), BEGIN, END],
                env=environment, capture_output=True, text=True, encoding="utf-8", check=False,
            )


@unittest.skipUnless(os.environ.get("PT_CADDY_DOCKER") == "1" and shutil.which("docker"),
                     "set PT_CADDY_DOCKER=1 with Docker available to serve the page from caddy:2")
class ServedPageTests(unittest.TestCase):
    """The rendered block, served by the real Caddy image over HTTP on a local port."""

    container = ""

    @classmethod
    def setUpClass(cls) -> None:
        cls.folder = tempfile.mkdtemp()
        rendered = pt_caddy_block.render_block(
            BLOCK_FILE.read_text(encoding="utf-8"), PAGE_FILE.read_text(encoding="utf-8"), "test-key"
        )
        # Plain HTTP on local ports instead of the public names; the apex keeps its handlers unchanged.
        rendered = rendered.replace("\nparking.pivotsoftwares.com {", "\nhttp://:8080 {")
        rendered = rendered.replace("\napi.parking.pivotsoftwares.com {", "\nhttp://:8081 {")
        rendered = rendered.replace("\nadmin.parking.pivotsoftwares.com {", "\nhttp://:8082 {")
        caddyfile = Path(cls.folder) / "Caddyfile"
        caddyfile.write_text("{\n\tauto_https off\n}\n\n" + rendered, encoding="utf-8", newline="\n")
        # The file is copied in, not bind-mounted, so the test also works when the Docker daemon is not on this host.
        created = subprocess.run(
            ["docker", "create", "-p", f"127.0.0.1:{LOCAL_PORT}:8080", CADDY_IMAGE],
            capture_output=True, text=True, check=True,
        )
        cls.container = created.stdout.strip()
        subprocess.run(["docker", "cp", str(caddyfile), f"{cls.container}:/etc/caddy/Caddyfile"], check=True)
        subprocess.run(["docker", "start", cls.container], capture_output=True, check=True)
        cls.wait_until_ready()

    @classmethod
    def tearDownClass(cls) -> None:
        if cls.container:
            subprocess.run(["docker", "rm", "-f", cls.container], capture_output=True, check=False)
        shutil.rmtree(cls.folder, ignore_errors=True)

    @classmethod
    def wait_until_ready(cls) -> None:
        for _ in range(20):
            try:
                cls.get("/")
                return
            except OSError:
                time.sleep(0.5)
        logs = subprocess.run(["docker", "logs", cls.container], capture_output=True, text=True, check=False)
        raise RuntimeError("Caddy did not answer: " + logs.stdout + logs.stderr)

    @staticmethod
    def get(path: str) -> http.client.HTTPResponse:
        connection = http.client.HTTPConnection("127.0.0.1", LOCAL_PORT, timeout=5)
        connection.request("GET", path, headers={"Accept-Encoding": "identity"})
        response = connection.getresponse()
        response.body = response.read()  # type: ignore[attr-defined]
        connection.close()
        return response

    def test_privacy_serves_the_file_byte_for_byte(self) -> None:
        response = self.get("/privacy")
        # Caddy drops the newline that ends a heredoc body, as escape_page does.
        expected = PAGE_FILE.read_text(encoding="utf-8").rstrip("\n").encode("utf-8")
        self.assertEqual(200, response.status)
        self.assertEqual("text/html; charset=utf-8", response.getheader("Content-Type"))
        self.assertEqual(expected, response.body.rstrip(b"\n"))

    def test_privacy_carries_the_security_headers(self) -> None:
        response = self.get("/privacy")
        self.assertEqual(
            "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            response.getheader("Content-Security-Policy"),
        )
        self.assertEqual("nosniff", response.getheader("X-Content-Type-Options"))
        self.assertEqual("no-referrer", response.getheader("Referrer-Policy"))
        self.assertEqual("max-age=31536000", response.getheader("Strict-Transport-Security"))

    def test_root_redirects_to_privacy(self) -> None:
        response = self.get("/")
        self.assertEqual(302, response.status)
        self.assertEqual("/privacy", response.getheader("Location"))

    def test_anything_else_is_not_found(self) -> None:
        for path in ("/other", "/privacy/", "/api"):
            with self.subTest(path=path):
                self.assertEqual(404, self.get(path).status)


if __name__ == "__main__":
    unittest.main()
