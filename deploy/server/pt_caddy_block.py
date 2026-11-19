#!/usr/bin/env python3
"""Writes the Parking Tracker block into the shared Caddyfile (ADR-0028); used by provision.sh and its tests.

Usage: pt_caddy_block.py <current Caddyfile> <block file> <privacy notice page> <begin marker> <end marker>
The proxy key comes from the environment (PT_PROXY_KEY), never from the argument list. The result goes to stdout and
nothing is written to disk; exit code 1 with a message on standard error when a file is missing or the page is unfit.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

PROXY_KEY_PLACEHOLDER = "PT_PROXY_KEY_PLACEHOLDER"
PAGE_PLACEHOLDER = "PT_PRIVACY_NOTICE_PLACEHOLDER"
HEREDOC_MARKER = "PT_PRIVACY_NOTICE_END"
KEY_PATTERN = re.compile(r"[A-Za-z0-9._~-]+")
PAGE_LINE = re.compile(rf"^{PAGE_PLACEHOLDER}$", re.MULTILINE)


class BlockError(Exception):
    """The block cannot be written; the message says why."""


def escape_page(page: str) -> str:
    r"""Returns the page as a Caddy `respond` heredoc body that serves the page's own bytes.

    Caddy expands {placeholders} in a response body and treats \{ and \} as literal braces, so each brace gets a
    backslash. A backslash in the page would change meaning next to a brace, so a page with one is refused.
    """
    if HEREDOC_MARKER in page:
        raise BlockError(f"the privacy notice contains the heredoc marker {HEREDOC_MARKER}")
    if PROXY_KEY_PLACEHOLDER in page:
        raise BlockError(f"the privacy notice contains {PROXY_KEY_PLACEHOLDER}")
    if "\\" in page:
        raise BlockError("the privacy notice contains a backslash, which Caddy would read as an escape")
    return page.rstrip("\n").replace("{", "\\{").replace("}", "\\}")


def check_key(proxy_key: str) -> None:
    """Refuses a key that is empty or holds a character that Caddy or the shell could read as syntax."""
    if not KEY_PATTERN.fullmatch(proxy_key):
        raise BlockError("the proxy key is empty or holds characters outside A-Z a-z 0-9 . _ ~ -")


def render_block(block: str, page: str, proxy_key: str) -> str:
    """Returns the block with the proxy key and the privacy notice in place of their placeholders."""
    check_key(proxy_key)
    if len(PAGE_LINE.findall(block)) != 1:
        raise BlockError(f"the block must hold a line with only {PAGE_PLACEHOLDER} exactly once")
    body = escape_page(page)
    # The key goes in first, so that text in the page can never be read as the key's placeholder.
    keyed = block.rstrip("\n").replace(PROXY_KEY_PLACEHOLDER, proxy_key) + "\n"
    # A function replacement keeps the page's characters literal; a string would be read for group references.
    return PAGE_LINE.sub(lambda _: body, keyed)


def merge_block(current: str, new_block: str, begin: str, end: str) -> str:
    """Replaces the block between the markers, or appends it when both markers are absent."""
    has_begin, has_end = begin in current, end in current
    if has_begin != has_end:
        raise BlockError("the shared Caddyfile holds only one of the Parking Tracker marker lines")
    if not has_begin:
        return current.rstrip("\n") + "\n\n" + new_block
    head, rest = current.split(begin, 1)
    if end not in rest:
        raise BlockError("the end marker line comes before the begin marker line in the shared Caddyfile")
    _, tail = rest.split(end, 1)
    return head + new_block + tail.lstrip("\n")


def main(argv: list[str]) -> int:
    """Reads the files named on the command line and prints the new Caddyfile."""
    parser = argparse.ArgumentParser(description="Writes the Parking Tracker block into the shared Caddyfile.")
    parser.add_argument("current", type=Path, help="the shared Caddyfile")
    parser.add_argument("block", type=Path, help="deploy/parking.Caddyfile")
    parser.add_argument("page", type=Path, help="deploy/site/privacy-notice.html")
    parser.add_argument("begin", help="the begin marker line")
    parser.add_argument("end", help="the end marker line")
    args = parser.parse_args(argv)
    try:
        new_block = render_block(
            args.block.read_text(encoding="utf-8"),
            args.page.read_text(encoding="utf-8"),
            os.environ["PT_PROXY_KEY"],
        )
        merged = merge_block(args.current.read_text(encoding="utf-8"), new_block, args.begin, args.end)
    except (BlockError, OSError) as error:
        print(f"pt_caddy_block: {error}", file=sys.stderr)
        return 1
    except KeyError:
        print("pt_caddy_block: PT_PROXY_KEY is not set", file=sys.stderr)
        return 1
    # Bytes, so that the output is UTF-8 whatever the console encoding is.
    sys.stdout.buffer.write(merged.encode("utf-8"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
