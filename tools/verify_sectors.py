"""Compare the street rules in sectors.js with the official City of Brussels PDFs.

Usage:  python tools/verify_sectors.py
Needs:  pdftotext (comes with Git for Windows / poppler / xpdf) on the PATH.

Exit code 0 = every street and every number range matches the official lists.
"""
import os
import re
import subprocess
import sys
import tempfile
import unicodedata
import urllib.request

BASE = "https://www.bruxelles.be/sites/default/files/bxl/"
SECTOR_PDF = BASE + "LOUISE.pdf"
TOLERANCE_PDF = BASE + "accord%20limitrophe%20Louise%20-%20Ixelles.pdf"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
END = 10**6  # "à fin" / "tot einde"; the tolerance PDF writes this as 999, 1000 or 10000
ALIASES = {"rue de tenbosch": "tenbosch"}


def norm(s):
    s = unicodedata.normalize("NFD", s)
    s = "".join(c for c in s if unicodedata.category(c) != "Mn").lower()
    s = re.sub(r"[’'`\-.]", " ", s)
    s = re.sub(r"\s+", " ", s).strip()
    return ALIASES.get(s, s)


def house_key(h, high=False):
    m = re.match(r"^(\d+)\s*([a-z]?)", h.strip().lower())
    n, l = int(m.group(1)), m.group(2)
    if n >= 999:
        return END
    return n * 100 + (ord(l) - 96 if l else (99 if high else 0))


def parse_rule(raw, empty_means_all):
    """Turn a cell into a sorted tuple of (low, high) keys. () = not covered."""
    s = (raw or "").strip().lower()
    if not s:
        return ((0, END),) if empty_means_all else ()
    if s.startswith("hors"):
        return ()
    out = []
    for part in re.split(r"\s+et\s+|,", s):
        part = part.strip()
        m = re.match(r"^(\S+)\s*(?:à|a)\s*fin", part)
        if m:
            out.append((house_key(m.group(1)), END))
            continue
        m = re.match(r"^(\S+?)\s*-\s*(\S+)$", part)
        if m:
            out.append((house_key(m.group(1)), house_key(m.group(2), True)))
            continue
        out.append((house_key(part), house_key(part, True)))
    return tuple(sorted(out))


FINGERPRINTS = {}  # url -> (etag, length, last-modified, sha256) of the downloaded file


def pdf_text(url):
    import hashlib
    with tempfile.TemporaryDirectory() as d:
        pdf = os.path.join(d, "f.pdf")
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=60) as r, open(pdf, "wb") as f:
            body = r.read()
            f.write(body)
            FINGERPRINTS[url] = (r.headers.get("ETag"), len(body), r.headers.get("Last-Modified"),
                                 hashlib.sha256(body).hexdigest())
        return subprocess.run(["pdftotext", "-table", "-enc", "UTF-8", pdf, "-"],
                              capture_output=True, check=True).stdout.decode("utf-8")


def check_sources():
    """The fingerprints in sectors.js must be those of the documents just verified."""
    src = open(os.path.join(ROOT, "sectors.js"), encoding="utf-8").read()
    errors = 0
    for url, (etag, length, modified, sha) in FINGERPRINTS.items():
        key = f"url: '{url}'"
        block = src[src.find(key):][:400] if key in src else ""
        ok = block and sha in block and str(length) in block and (etag or "") in block and (modified or "") in block
        if not ok:
            errors += 1
            print(f"  SOURCE FINGERPRINT out of date for {url}\n     put in sectors.js: etag: '{etag}', length: {length},"
                  f" lastModified: '{modified}', sha256: '{sha}'")
    print(f"Source fingerprints: {len(FINGERPRINTS)} checked, {errors} problem(s)")
    return errors


def official_sector():
    rows = {}
    for line in pdf_text(SECTOR_PDF).splitlines():
        cells = [c for c in re.split(r"\s{2,}", line.strip()) if c]
        if len(cells) in (2, 4) and re.match(r"^(Rue|Avenue|Allée|Boulevard|Chaussée|Place|Tenbosch)", cells[0]):
            odd, even = (cells[2], cells[3]) if len(cells) == 4 else ("", "")
            rows[norm(cells[0])] = (cells[0], parse_rule(odd, True), parse_rule(even, True))
    return rows


def official_tolerance():
    """Ixelles-side table: columns are odd-from, odd-to, even-from, even-to (by position)."""
    text = pdf_text(TOLERANCE_PDF)
    lines = text.splitlines()
    start = next(i for i, l in enumerate(lines) if "territoire d'Ixelles" in l or "territoire d’Ixelles" in l)
    rows = {}
    for line in lines[start:]:
        m = re.search(r"\b1050\b", line)
        if "BXL_LIM" not in line or not m:
            continue
        name = re.split(r"\s{2,}", line.strip())[1]
        tail = line[m.end():]
        nums = [(x.start(), x.group()) for x in re.finditer(r"\S+", tail)]
        odd, even = [], []
        for pos, val in nums:
            (odd if pos < 28 else even).append(val)
        key = norm(name)
        prev = rows.get(key, (name, [], []))
        for side, vals in ((prev[1], odd), (prev[2], even)):
            for a, b in zip(vals[::2], vals[1::2]):
                lo, hi = sorted([house_key(a), house_key(b, True)], key=lambda k: k)
                if house_key(a) > house_key(b):  # written backwards, e.g. 76 → 40
                    lo, hi = house_key(b), house_key(a, True)
                side.append((lo, hi))
        rows[key] = prev
    return {k: (v[0], tuple(sorted(v[1])), tuple(sorted(v[2]))) for k, v in rows.items()}


def app_tables():
    src = open(os.path.join(ROOT, "sectors.js"), encoding="utf-8").read()
    row = re.compile(r"""\[\s*(['"])(.+?)\1,\s*(['"])(.+?)\3,\s*'(.*?)',\s*'(.*?)'\s*\]""")
    base_src, _, tol_src = src.partition("tolerances:")
    base = {norm(m.group(2)): (m.group(2), parse_rule(m.group(5), True), parse_rule(m.group(6), True))
            for m in row.finditer(base_src)}
    tol = {norm(m.group(2)): (m.group(2), parse_rule(m.group(5), False), parse_rule(m.group(6), False))
           for m in row.finditer(tol_src)}
    return base, tol


def compare(label, official, app):
    errors = 0
    for k in sorted(set(official) | set(app)):
        if k not in app:
            print(f"  MISSING in app: {official[k][0]}"); errors += 1
        elif k not in official:
            print(f"  NOT in official list: {app[k][0]}"); errors += 1
        elif official[k][1:] != app[k][1:]:
            print(f"  DIFFERENT: {app[k][0]}\n     official odd={official[k][1]} even={official[k][2]}"
                  f"\n     app      odd={app[k][1]} even={app[k][2]}"); errors += 1
    print(f"{label}: {len(official)} official streets, {len(app)} in app, {errors} problem(s)")
    return errors


def main():
    base, tol = app_tables()
    errors = compare("Sector Louise", official_sector(), base)
    errors += compare("Ixelles – Louise tolerance", official_tolerance(), tol)
    errors += check_sources()
    print("ALL MATCH ✓" if not errors else "MISMATCHES FOUND ✗")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
