"""
SVG -> DXF converter for the ONE PROCESS block icons, targeting the Aspen Plus
V14 Icon Editor (Customize > user library > Edit icon > Import DXF).

The Aspen Icon Editor imports AutoCAD DXF and renders filled polylines and
solid hatches, but it does NOT read raster images. This converter flattens each
icon's shapes (rect / circle / path with M,L,H,V,C,A,Z) to polylines, emits a
HATCH for every filled shape in the icon's own colour, and a polyline outline
for every stroke, so the imported icon keeps the filled flat-vector look.

Aspen icon space: we map the 0..64 SVG viewBox to a centred +/-1.0 icon square
(Aspen icons are unit-ish and Y-up), so the icon lands sensibly in the editor.

Usage:  python svg_to_dxf.py <svgDir> <outDir>
"""
import os
import sys
import math
import re
import xml.etree.ElementTree as ET

import ezdxf
from svgpathtools import parse_path

# ONE PROCESS palette -> nearest AutoCAD Color Index (ACI) the Aspen editor uses.
FILL_ACI = {
    "#BFE3F5": 151,  # light blue
    "#29ABE2": 150,  # cyan accent
    "#FFFFFF": 7,    # white
    "#1B3A5C": 18,   # dark navy
    "#FFF8E1": 51,
}
OUTLINE_ACI = 18     # dark navy outline
SEG = 14             # flattening segments per curve/arc


def aci(hexcol, table, default):
    if not hexcol:
        return default
    h = hexcol.strip().upper()
    return table.get(h, default)


def transform(pt, vb):
    """Map an SVG point (0..64, y-down) to Aspen icon space (+/-1, y-up)."""
    x, y = pt
    s = 2.0 / vb
    return (x * s - 1.0, 1.0 - y * s)


def flatten_path(d, vb):
    """Return a list of polylines (each a list of xy) from an SVG path d-string."""
    polylines = []
    try:
        path = parse_path(d)
    except Exception:
        return polylines
    cur = []
    for seg in path:
        # start a new polyline on a moveto gap
        p0 = (seg.start.real, seg.start.imag)
        if not cur:
            cur = [transform(p0, vb)]
        elif abs(complex(*(_untransform(cur[-1], vb))) - seg.start) > 1e-6:
            if len(cur) > 1:
                polylines.append(cur)
            cur = [transform(p0, vb)]
        n = 1 if seg.__class__.__name__ == "Line" else SEG
        for i in range(1, n + 1):
            pt = seg.point(i / n)
            cur.append(transform((pt.real, pt.imag), vb))
    if len(cur) > 1:
        polylines.append(cur)
    return polylines


def _untransform(pt, vb):
    x, y = pt
    s = vb / 2.0
    return ((x + 1.0) * s, (1.0 - y) * s)


def rect_points(el, vb):
    x = float(el.get("x", 0)); y = float(el.get("y", 0))
    w = float(el.get("width", 0)); h = float(el.get("height", 0))
    rx = float(el.get("rx", 0) or 0)
    if rx <= 0:
        pts = [(x, y), (x + w, y), (x + w, y + h), (x, y + h), (x, y)]
        return [transform(p, vb) for p in pts]
    # rounded rect -> flatten the four corner arcs
    rx = min(rx, w / 2); ry = rx
    pts = []
    def arc(cx, cy, a0, a1):
        for i in range(SEG + 1):
            a = math.radians(a0 + (a1 - a0) * i / SEG)
            pts.append((cx + rx * math.cos(a), cy + ry * math.sin(a)))
    pts.append((x + rx, y))
    pts.append((x + w - rx, y))
    arc(x + w - rx, y + ry, -90, 0)
    pts.append((x + w, y + h - ry))
    arc(x + w - rx, y + h - ry, 0, 90)
    pts.append((x + rx, y + h))
    arc(x + rx, y + h - ry, 90, 180)
    pts.append((x, y + ry))
    arc(x + rx, y + ry, 180, 270)
    pts.append((x + rx, y))
    return [transform(p, vb) for p in pts]


def circle_points(el, vb):
    cx = float(el.get("cx", 0)); cy = float(el.get("cy", 0)); r = float(el.get("r", 0))
    pts = []
    for i in range(SEG * 2 + 1):
        a = 2 * math.pi * i / (SEG * 2)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return [transform(p, vb) for p in pts]


def collect(el, inherited, vb, shapes):
    """Walk the SVG tree; append (kind, points, fill_aci, stroke_aci) shapes."""
    fill = el.get("fill", inherited.get("fill"))
    stroke = el.get("stroke", inherited.get("stroke"))
    tag = el.tag.split("}")[-1]
    child_inherit = {"fill": fill, "stroke": stroke}

    polys = []
    closed = False
    if tag == "rect":
        polys = [rect_points(el, vb)]; closed = True
    elif tag == "circle":
        polys = [circle_points(el, vb)]; closed = True
    elif tag == "path":
        d = el.get("d", "")
        polys = flatten_path(d, vb)
        closed = "z" in d.lower()

    for poly in polys:
        f = None if (not fill or fill == "none") else aci(fill, FILL_ACI, 151)
        s = None if (not stroke or stroke == "none") else OUTLINE_ACI
        if f is None and s is None and tag in ("rect", "circle", "path"):
            s = OUTLINE_ACI  # default visible outline
        shapes.append((poly, f, s, closed))

    for child in el:
        collect(child, child_inherit, vb, shapes)


def convert(svg_path, dxf_path):
    tree = ET.parse(svg_path)
    root = tree.getroot()
    vbattr = root.get("viewBox", "0 0 64 64").split()
    vb = float(vbattr[2])  # assume square viewBox

    shapes = []
    collect(root, {"fill": None, "stroke": None}, vb, shapes)

    doc = ezdxf.new("R2000")   # R2000 supports HATCH fills; most importers read it
    msp = doc.modelspace()
    # fills first (so outlines draw on top)
    for poly, f, s, closed in shapes:
        if f is not None and len(poly) >= 3:
            hatch = msp.add_hatch(color=f)
            path = hatch.paths.add_polyline_path(
                [(x, y) for x, y in poly], is_closed=True)
    for poly, f, s, closed in shapes:
        if s is not None and len(poly) >= 2:
            msp.add_lwpolyline(
                [(x, y) for x, y in poly], close=closed,
                dxfattribs={"color": s})
    doc.saveas(dxf_path)
    return len(shapes)


def main():
    svg_dir = sys.argv[1]
    out_dir = sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    n = 0
    for name in sorted(os.listdir(svg_dir)):
        if not name.lower().endswith(".svg"):
            continue
        src = os.path.join(svg_dir, name)
        dst = os.path.join(out_dir, os.path.splitext(name)[0] + ".dxf")
        try:
            shapes = convert(src, dst)
            print(f"ok  {name}  ({shapes} shapes)")
            n += 1
        except Exception as e:
            print(f"ERR {name}: {type(e).__name__} {e}")
    print(f"converted {n} icons")


if __name__ == "__main__":
    main()
