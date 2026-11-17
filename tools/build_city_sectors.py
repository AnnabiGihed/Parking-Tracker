"""Build city-sectors.json from the City of Brussels official sector PDFs (one per sector).

Usage:  python tools/build_city_sectors.py          # writes the JSON that ParkingTracker.Infrastructure embeds
        python tools/build_city_sectors.py --check  # exit 1 if the JSON differs from the PDFs

Needs:  pdftotext (Git for Windows / poppler / xpdf) on the PATH.
Exit code: 0 = done / up to date, 1 = --check found a difference, 2 = an existing output file cannot be read or written.
The output is written as a temporary file first and then replaced, so a failure cannot leave a half-written file.

Each PDF lists streets with the odd and even house numbers that belong to the sector. Cells that are not
plain number ranges (e.g. "côté parc", "entre … et …") are kept as text and the app answers "check the signs".
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_JSON = os.path.join(ROOT, "src", "Infrastructure", "ParkingTracker.Infrastructure", "Data", "city-sectors.json")
BASE = "https://www.bruxelles.be/sites/default/files/bxl/"
SECTORS = [  # id, name FR, name NL, PDF file (street list) — from bruxelles.be/ou-stationner-avec-sa-carte-de-stationnement
    ("pentagone", "Pentagone", "Vijfhoek", "PENTAGONE.pdf"),
    ("nord", "Nord", "Noord", "NORD_2.pdf"),
    ("schuman", "Schuman - Europe - Squares", "Schuman - Europa - Squares", "SCHUMAN.pdf"),
    ("louise", "Louise", "Louiza", "LOUISE.pdf"),
    ("roosevelt", "Roosevelt - Vert Chasseur", "Roosevelt - Groenejagersveld", "ROOSEVELT.pdf"),
    ("bois-cambre", "Bois de la Cambre", "Terkamerenbos", "BOIS%20DE%20LA%20CAMBRE.pdf"),
    ("laeken", "Laeken", "Laken", "LAEKEN.pdf"),
    ("heysel", "Heysel - Verregat", "Heizel - Verregat", "HEYSEL%20VERREGAT.pdf"),
    ("noh", "De Wand - Croix de Feu - Neder-Over-Heembeek", "De Wand - Vuurkruisen - Neder-Over-Heembeek", "DE%20WAND%20NOH_0.pdf"),
    ("haren", "Haren", "Haren", "HAREN.pdf"),
]
STREET_START = re.compile(r"^[A-ZÀ-Ý]")
NUM = r"\d+\s?[A-Za-z]?"
RANGE = re.compile(rf"^({NUM})\s*-\s*({NUM})$")
FROM_END = re.compile(rf"^({NUM})\s*(?:à|a)\s*fin$", re.I)
SINGLE = re.compile(rf"^({NUM})$")
LIST = re.compile(r"^\d+[A-Za-z]?(\s*[,&]\s*\d+[A-Za-z]?)+$")


def download(file):
    req = urllib.request.Request(BASE + file, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=60) as r:
        body = r.read()
        return body, {"etag": r.headers.get("ETag"), "length": len(body), "lastModified": r.headers.get("Last-Modified"),
                      "sha256": hashlib.sha256(body).hexdigest(), "url": BASE + file}


def pdf_lines(body):
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "f.pdf")
        open(p, "wb").write(body)
        return subprocess.run(["pdftotext", "-table", "-enc", "UTF-8", p, "-"], capture_output=True, check=True).stdout.decode("utf-8").split("\f")


def clean_value(v):
    """Turn a cell into the app's rule syntax ('' = whole side, 'hors', '1-13', '59 à fin', 'a et b'), or 'check: text'."""
    v = re.sub(r"\s+", " ", v).strip(" /")
    v = re.sub(r"^(\S+)\s*-\s*(\S+)$", r"\1-\2", v)
    v = re.sub(r"\s*/\s*(tot\s+einde|buiten\s+zone|oneven|even)\b.*$", "", v, flags=re.I).strip()
    if not v:
        return ""
    low = v.lower()
    if low.startswith("hors"):
        return "hors"
    if low in ("impair", "impairs", "pair", "pairs"):
        return ""
    parts, out = re.split(r"\s+et\s+", v), []
    for p in parts:
        p = p.strip()
        m = RANGE.match(p)
        if m:
            out.append(f"{m.group(1).replace(' ', '')}-{m.group(2).replace(' ', '')}"); continue
        m = FROM_END.match(p)
        if m:
            out.append(f"{m.group(1).replace(' ', '')} à fin"); continue
        if SINGLE.match(p):
            out.append(p.replace(" ", "")); continue
        if LIST.match(p):
            out.append(" et ".join(x.strip() for x in re.split(r"[,&]", p))); continue
        return "check: " + v
    return " et ".join(out)


def parse(body):
    streets = []
    for page in pdf_lines(body):
        lines = [l for l in page.splitlines() if l.strip()]
        rows = []  # (fr, cp, nl, [(pos, text)])
        for line in lines:
            if re.search(r"Noms des rues|Straatnaam|Numéros|nummers van|UNIQUEMENT|^\s*\*|ALLEEN TUSSEN", line):
                continue
            # Blank out Dutch duplicates without moving the columns ("/ buiten zone", "/ tot einde").
            norm = re.sub(r"/\s*(tot\s+einde|buiten\s+zone)", lambda m: " " * len(m.group()), line)
            norm = re.sub(r"au\s*/\s*tot", lambda m: "-".ljust(len(m.group())), norm)
            # "Rue  de Pavie": a street type followed by a double space belongs to the same name.
            norm = re.sub(r"^(Rue|Avenue|Place|Boulevard|Chaussée|Square|Quai|Impasse|Allée|Parvis|Cité|Drève|Chemin)  +(?=\S)",
                          lambda m: m.group(1) + " ", norm)
            cells = [(m.start(), m.group()) for m in re.finditer(r"\S+(?: \S+)*", norm)]
            if not cells:
                continue
            if norm[0] != " " and STREET_START.match(cells[0][1]):
                fr = cells[0][1]
                rest = cells[1:]
                cp = None
                if rest and re.fullmatch(r"\d{4}(\+\d{4})?", rest[0][1]):
                    cp = rest[0][1]; rest = rest[1:]
                if not rest:
                    rows.append([fr, cp, None, []]); continue
                nl = rest[0][1]
                rows.append([fr, cp, nl, rest[1:]])
            elif rows:  # continuation of the previous row (wrapped text)
                rows[-1][3].extend(cells)
        # Column positions of odd / even values on this page, learnt from rows that have two values.
        two = [r[3] for r in rows if len(r[3]) == 2]
        if two:
            odd_x = sorted(c[0][0] for c in two)[len(two) // 2]
            even_x = sorted(c[1][0] for c in two)[len(two) // 2]
        else:
            odd_x, even_x = 60, 90
        for fr, cp, nl, vals in rows:
            odd, even, extra = [], [], []
            for pos, text in vals:
                if text in ("/",):
                    continue
                (odd if abs(pos - odd_x) <= abs(pos - even_x) else even).append(text)
            o = clean_value(" ".join(odd))
            e = clean_value(" ".join(even))
            # A condition in words on one side ("côté parc", "uniquement de 9h à 10h"…) makes an empty other side
            # unreliable (empty would mean "whole side"): ask to check the signs on both sides.
            if o.startswith("check:") and e == "":
                e = o
            elif e.startswith("check:") and o == "":
                o = e
            # A list like "7,8,9,10 & 11" mixes odd and even numbers (squares): apply it to both sides.
            for side_val in (o, e):
                if re.fullmatch(r"\d+[a-z]?( et \d+[a-z]?){2,}", side_val):
                    o = e = side_val
            streets.append([fr, nl or fr, o, e] + ([cp] if cp else []))
    return streets


def build():
    data = {}
    for sid, name, name_nl, file in SECTORS:
        body, fp = download(file)
        data[sid] = {"id": sid, "name": name, "nameNl": name_nl, "municipality": "bruxelles", "kind": "streets",
                     "sources": [fp], "streets": parse(body)}
    return data


def render_json(data):
    return json.dumps(data, ensure_ascii=False, indent=1) + "\n"


def read_existing(path):
    """The current content of an output file, '' when it does not exist yet; exits with a message when unreadable."""
    if not os.path.exists(path):
        return ""
    try:
        with open(path, encoding="utf-8") as f:
            return f.read()
    except (OSError, UnicodeDecodeError) as e:
        print(f"ERROR: cannot read {os.path.relpath(path, ROOT)}: {e}", file=sys.stderr)
        sys.exit(2)


def write_atomically(outputs):
    """Write every output to a temporary file next to it, then replace all targets; remove leftovers on failure."""
    temps = []
    try:
        for path, text in outputs:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            fd, tmp = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".tmp")
            temps.append((tmp, path))
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                f.write(text)
        while temps:
            tmp, path = temps[0]
            os.replace(tmp, path)
            temps.pop(0)
    except OSError as e:
        print(f"ERROR: cannot write the outputs: {e}", file=sys.stderr)
        sys.exit(2)
    finally:
        for tmp, _ in temps:
            if os.path.exists(tmp):
                os.remove(tmp)


def main():
    data = build()
    outputs = [(OUT_JSON, render_json(data))]
    if "--check" in sys.argv:
        strip = lambda t: re.sub(r'"(etag|lastModified|length)": [^,\n]+,?', "", t)
        same = True
        for path, text in outputs:
            old = read_existing(path)
            ok = strip(old) == strip(text)
            same = same and ok
            name = os.path.relpath(path, ROOT)
            print(f"{name} matches the official PDFs ✓" if ok else f"{name} is OUT OF DATE ✗ (run without --check)")
        sys.exit(0 if same else 1)
    write_atomically(outputs)
    for sid, s in data.items():
        checks = sum(1 for r in s["streets"] for v in r[2:4] if v.startswith("check:"))
        print(f"{sid:12} {len(s['streets']):4} streets, {checks} with a condition in words")


if __name__ == "__main__":
    main()
