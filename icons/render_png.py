# -*- coding: utf-8 -*-
"""
icons/OP-*.svg  ->  icons/png/OP-*.png  (256x256, transparent).

The DWSIM adapter embeds icons/png/OP-*.png as resources and package-blocks.ps1
copies them beside each family DLL, so this must run after make_icons.py.

No cairo/librsvg on this machine (cairosvg and svglib both need a native
backend that is absent), so rasterizing goes through headless Chrome/Edge —
already installed, and it is the same renderer the SVGs were designed against.
All icons are laid out on ONE page and cropped afterwards: a single browser
launch instead of 26, and every icon is guaranteed to be rendered identically.
"""
import base64
import glob
import os
import subprocess
import sys
import tempfile

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUTDIR = os.path.join(HERE, "png")
CELL = 256
COLS = 6

BROWSERS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]


def browser():
    for b in BROWSERS:
        if os.path.exists(b):
            return b
    sys.exit("no Chrome/Edge found for rasterizing")


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    svgs = sorted(glob.glob(os.path.join(HERE, "OP-*.svg")))
    if not svgs:
        sys.exit("no icons/OP-*.svg — run make_icons.py first")

    rows = (len(svgs) + COLS - 1) // COLS
    cells = []
    for p in svgs:
        with open(p, encoding="utf-8") as f:
            svg = f.read()
        # inline as a data: URI so the browser cannot re-order or cache-miss them
        b64 = base64.b64encode(svg.encode("utf-8")).decode("ascii")
        cells.append(f'<img src="data:image/svg+xml;base64,{b64}" width="{CELL}" height="{CELL}">')

    html = (
        "<!doctype html><meta charset='utf-8'>"
        "<style>html,body{margin:0;padding:0;background:transparent}"
        f"#g{{display:grid;grid-template-columns:repeat({COLS},{CELL}px);"
        f"grid-auto-rows:{CELL}px}}img{{display:block}}</style>"
        "<div id='g'>" + "".join(cells) + "</div>"
    )
    tmp = os.path.join(tempfile.gettempdir(), "opblocks_icon_sheet.html")
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(html)

    sheet = os.path.join(tempfile.gettempdir(), "opblocks_icon_sheet.png")
    if os.path.exists(sheet):
        os.remove(sheet)
    subprocess.run([
        browser(), "--headless", "--disable-gpu", "--hide-scrollbars",
        "--force-device-scale-factor=1",
        "--default-background-color=00000000",
        f"--screenshot={sheet}",
        f"--window-size={COLS * CELL},{rows * CELL}",
        "file:///" + tmp.replace("\\", "/"),
    ], check=True, capture_output=True, timeout=180)

    img = Image.open(sheet).convert("RGBA")
    for i, p in enumerate(svgs):
        x, y = (i % COLS) * CELL, (i // COLS) * CELL
        code = os.path.splitext(os.path.basename(p))[0]
        img.crop((x, y, x + CELL, y + CELL)).save(os.path.join(OUTDIR, code + ".png"))
    print(f"rendered {len(svgs)} icons -> {OUTDIR}")


if __name__ == "__main__":
    main()
