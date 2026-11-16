"""Generate all app icons (web + Android launcher + Android splash) without extra libraries.

Usage:  python tools/make_icons.py
"""
import math
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "android", "app", "src", "main", "res")
BLUE = (37, 99, 235, 255)
WHITE = (255, 255, 255, 255)
DARK = (15, 23, 42, 255)
CLEAR = (0, 0, 0, 0)


def write_png(path, w, h, rows):
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + r for r in rows)
    data = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24])


def in_p(x, y, ox, oy, s):
    """White letter P inside the square (ox, oy, s)."""
    x0, y0, x1, y1 = ox + s * 0.22, oy + s * 0.1, ox + s * 0.40, oy + s * 0.9
    if x0 <= x <= x1 and y0 <= y <= y1:
        return True
    cx, cy, ro, ri = ox + s * 0.47, oy + s * 0.36, s * 0.27, s * 0.11
    d = math.hypot(x - cx, y - cy)
    if ri <= d <= ro and x >= cx:
        return True
    if (cy - ro <= y <= cy - ri or cy + ri <= y <= cy + ro) and x0 <= x <= cx:
        return True
    return False


def in_round_rect(x, y, x0, y0, x1, y1, r):
    if not (x0 <= x <= x1 and y0 <= y <= y1):
        return False
    cx, cy = min(max(x, x0 + r), x1 - r), min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def render(w, h, bg, tile, tile_frac, round_frac, p_frac, circle=False):
    """bg fills the image; a tile (rounded square / circle) of tile_frac*min(w,h) is centred; P inside."""
    m = min(w, h)
    ts = m * tile_frac
    tx, ty = (w - ts) / 2, (h - ts) / 2
    ps = ts * p_frac
    px, py = (w - ps) / 2, (h - ps) / 2
    bg_row = bytes(bg) * w
    rows = []
    for j in range(h):
        y = j + 0.5
        if y < ty or y > ty + ts:
            rows.append(bg_row)
            continue
        row = bytearray(bg_row)
        for i in range(int(tx), min(w, int(tx + ts) + 1)):
            x = i + 0.5
            if circle:
                inside = (x - w / 2) ** 2 + (y - h / 2) ** 2 <= (ts / 2) ** 2
            else:
                inside = in_round_rect(x, y, tx, ty, tx + ts, ty + ts, ts * round_frac)
            if tile is not None and inside:
                row[i * 4:i * 4 + 4] = bytes(tile)
            if (inside or tile is None) and in_p(x, y, px, py, ps):
                row[i * 4:i * 4 + 4] = bytes(WHITE)
        rows.append(bytes(row))
    return rows


def main():
    # Web / PWA
    icons = os.path.join(ROOT, "icons")
    for name, size in (("icon-192.png", 192), ("icon-512.png", 512), ("apple-touch-icon.png", 180)):
        write_png(os.path.join(icons, name), size, size, render(size, size, CLEAR, BLUE, 0.88, 0.25, 0.85))
    write_png(os.path.join(icons, "maskable-512.png"), 512, 512, render(512, 512, BLUE, BLUE, 1, 0, 0.6))

    if not os.path.isdir(RES):
        print("Web icons done (no android/ project yet).")
        return
    # Android launcher icons: legacy square + round, and adaptive foreground (108dp canvas).
    for dens, size in (("mdpi", 48), ("hdpi", 72), ("xhdpi", 96), ("xxhdpi", 144), ("xxxhdpi", 192)):
        d = os.path.join(RES, f"mipmap-{dens}")
        write_png(os.path.join(d, "ic_launcher.png"), size, size, render(size, size, CLEAR, BLUE, 0.92, 0.22, 0.85))
        write_png(os.path.join(d, "ic_launcher_round.png"), size, size, render(size, size, CLEAR, BLUE, 0.92, 0, 0.8, circle=True))
        fg = size * 108 // 48
        write_png(os.path.join(d, "ic_launcher_foreground.png"), fg, fg, render(fg, fg, CLEAR, None, 1, 0, 0.5))
    with open(os.path.join(RES, "values", "ic_launcher_background.xml"), "w", encoding="utf-8") as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n<resources>\n    <color name="ic_launcher_background">#2563EB</color>\n</resources>\n')
    # Splash screens: keep each existing size, dark background with the icon in the middle.
    for folder in sorted(os.listdir(RES)):
        path = os.path.join(RES, folder, "splash.png")
        if folder.startswith("drawable") and os.path.exists(path):
            w, h = png_size(path)
            write_png(path, w, h, render(w, h, DARK, BLUE, 0.28, 0.25, 0.85))
    print("Web + Android icons and splash screens written.")


if __name__ == "__main__":
    main()
