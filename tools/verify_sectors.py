"""Compare the street rules in tolerance-sectors.json with the official City of Brussels PDFs.

Usage:  python tools/verify_sectors.py
Needs:  pdftotext (comes with Git for Windows / poppler / xpdf) on the PATH.

Also checks that the City sectors JSON (city-sectors.json) agrees with tolerance-sectors.json for the Louise sector.

Exit code 0 = every street and every number range matches the official lists and the two files agree.
Exit code 1 = a mismatch; exit code 2 = an input file is missing or unreadable.
"""
import json
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
DATA = os.path.join(ROOT, "src", "Infrastructure", "ParkingTracker.Infrastructure", "Data", "tolerance-sectors.json")
CITY_JSON = os.path.join(os.path.dirname(DATA), "city-sectors.json")
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


def fail_input(message):
    print(f"ERROR: {message}", file=sys.stderr)
    sys.exit(2)


def read_text(path):
    try:
        with open(path, encoding="utf-8") as f:
            return f.read()
    except (OSError, UnicodeDecodeError) as e:
        fail_input(f"cannot read {os.path.relpath(path, ROOT)}: {e}")


def read_json(path):
    try:
        return json.loads(read_text(path))
    except ValueError as e:
        fail_input(f"{os.path.relpath(path, ROOT)} is not valid JSON: {e}")


def load_data():
    try:
        return read_json(DATA)["louise"]
    except KeyError:
        fail_input(f"{os.path.relpath(DATA, ROOT)} has no 'louise' sector")


def check_sources(louise):
    """The fingerprints in tolerance-sectors.json must be those of the documents just verified."""
    known = {s["url"]: s for s in louise["sources"]}
    errors = 0
    for url, (etag, length, modified, sha) in FINGERPRINTS.items():
        s = known.get(url, {})
        if (s.get("etag"), s.get("length"), s.get("lastModified"), s.get("sha256")) != (etag, length, modified, sha):
            errors += 1
            print(f"  SOURCE FINGERPRINT out of date for {url}\n     put in tolerance-sectors.json: etag: {etag!r}, length: {length},"
                  f" lastModified: {modified!r}, sha256: '{sha}'")
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


def app_tables(louise):
    def table(rows, empty_means_all):
        return {norm(r[0]): (r[0], parse_rule(r[2], empty_means_all), parse_rule(r[3], empty_means_all)) for r in rows}
    return table(louise["streets"], True), table(louise["tolerances"][0]["streets"], False)


def json_rows(rows):
    return [tuple(r[:4]) for r in rows]


def compare_rows(label, left_name, left, right_name, right):
    """Both lists must hold the same raw rows in the same order."""
    errors = 0
    for row in left:
        if row not in right:
            print(f"  {label}: only in {left_name}: {row}"); errors += 1
    for row in right:
        if row not in left:
            print(f"  {label}: only in {right_name}: {row}"); errors += 1
    if not errors and left != right:
        print(f"  {label}: same rows but in a different order in {left_name} and {right_name}"); errors += 1
    print(f"{label}: {len(left)} rows in {left_name}, {len(right)} in {right_name}, {errors} problem(s)")
    return errors


def check_copies(louise):
    """The City sectors JSON and tolerance-sectors.json must say the same about the Louise sector."""
    city = read_json(CITY_JSON).get("louise")
    if city is None:
        fail_input(f"{os.path.relpath(CITY_JSON, ROOT)} has no 'louise' sector")
    return compare_rows("Louise city-sectors.json vs tolerance-sectors.json", "city", json_rows(city["streets"]),
                        "tolerance", json_rows(louise["streets"]))


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
    louise = load_data()
    base, tol = app_tables(louise)
    errors = compare("Sector Louise", official_sector(), base)
    errors += compare("Ixelles – Louise tolerance", official_tolerance(), tol)
    errors += check_sources(louise)
    errors += check_copies(louise)
    print("ALL MATCH ✓" if not errors else "MISMATCHES FOUND ✗")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
