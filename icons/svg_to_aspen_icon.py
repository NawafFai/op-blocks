"""
SVG -> Aspen Plus icon script generator + .apm injector.

Discovery (2026-07-11, on the owner's machine): Aspen stores each user-model
icon as a plain-text drawing script inside the model's CONTENTS stream in the
.apm OLE compound file. The script vocabulary (from iconed.dll + sample .apm
files) is:

    sub main
      call Path.Begin
      '' <<Path:0
      call Path.Box(x1,y1,x2,y2,0,0,1)
      call Path.Ellipse(cx,cy,rx,ry,0,0,0,1)
      call Path.Line(x1,y1,x2,y2,0,1)
      call Path.Polyline(...)          ' (n) pairs
      call Path.Text(x,y,"txt",ang,"font",size,0,-1)
      '' >>
      call Path.End
    end sub

Icon space is a centred square, roughly -0.45..0.45, Y-up. Aspen flowsheet
icons are monochrome line art (no per-shape colour), so each block is drawn as
its distinct outline+detail silhouette flattened from the SVG — far better than
the generic CAPE-OPEN rectangle, and generated for all 25 with zero manual
drawing.

This module:
  1. parses an icon SVG (rect/circle/path with M,L,H,V,C,A,Z),
  2. emits the Aspen `sub main` drawing script,
  3. rewrites the CONTENTS stream of a model inside a ONE PROCESS.apm,
     preserving the ports/label/model-type sections around it.
"""
import os
import sys
import math
import re
import xml.etree.ElementTree as ET

from svgpathtools import parse_path

MARGIN = 0.86          # fill fraction of the -0.5..0.5 icon square
SEG = 10               # flattening segments per curve/arc


def _map(x, y, vb):
    """SVG (0..vb, y-down) -> Aspen icon space (centred, Y-up)."""
    return ((x / vb - 0.5) * MARGIN, (0.5 - y / vb) * MARGIN)


def _fmt(v):
    return ("%.5f" % v).rstrip("0").rstrip(".") or "0"


def _poly(points):
    """Path.Polyline over a list of (x,y) icon-space points."""
    if len(points) < 2:
        return []
    coords = ",".join(_fmt(c) for pt in points for c in pt)
    return ["call Path.Polyline(%d,%s,0,1)" % (len(points), coords)]


def _flatten_path(d, vb):
    polylines = []
    try:
        path = parse_path(d)
    except Exception:
        return polylines
    cur = []
    for seg in path:
        start = (seg.start.real, seg.start.imag)
        if not cur:
            cur = [_map(*start, vb)]
        n = 1 if seg.__class__.__name__ == "Line" else SEG
        for i in range(1, n + 1):
            pt = seg.point(i / n)
            cur.append(_map(pt.real, pt.imag, vb))
        if seg.__class__.__name__ != "Line" or True:
            pass
    if len(cur) > 1:
        polylines.append(cur)
    return polylines


def _shape_cmds(el, vb):
    tag = el.tag.split("}")[-1]
    out = []
    if tag == "rect":
        x = float(el.get("x", 0)); y = float(el.get("y", 0))
        w = float(el.get("width", 0)); h = float(el.get("height", 0))
        x1, y1 = _map(x, y, vb)
        x2, y2 = _map(x + w, y + h, vb)
        out.append("call Path.Box(%s,%s,%s,%s,0,0,1)" % (_fmt(x1), _fmt(y1), _fmt(x2), _fmt(y2)))
    elif tag == "circle":
        cx = float(el.get("cx", 0)); cy = float(el.get("cy", 0)); r = float(el.get("r", 0))
        cxm, cym = _map(cx, cy, vb)
        rx = r / vb * MARGIN
        out.append("call Path.Ellipse(%s,%s,%s,%s,0,0,0,1)" % (_fmt(cxm), _fmt(cym), _fmt(rx), _fmt(rx)))
    elif tag == "path":
        for poly in _flatten_path(el.get("d", ""), vb):
            out += _poly(poly)
    return out


def _walk(el, vb, out):
    out += _shape_cmds(el, vb)
    for child in el:
        _walk(child, vb, out)


def svg_to_script(svg_path):
    root = ET.parse(svg_path).getroot()
    vb = float(root.get("viewBox", "0 0 64 64").split()[2])
    body = []
    _walk(root, vb, body)
    lines = ["sub main", "call Path.Begin", "'' <<Path:0"]
    lines += body
    lines += ["'' >>", "call Path.End", "end sub"]
    return "\r\n".join(lines) + "\r\n"


# ------------------------------------------------------------------
#  .apm CONTENTS rewrite — replace only the `sub main ... end sub`
# ------------------------------------------------------------------

def _read_all_streams(apm_path):
    """Return {('OP-RO','CONTENTS'): bytes, ...} for every stream in the .apm."""
    import olefile
    ole = olefile.OleFileIO(apm_path)
    streams = {}
    for entry in ole.listdir():
        streams[tuple(entry)] = ole.openstream(entry).read()
    ole.close()
    return streams


def _modified_contents(raw, script):
    """Replace the sub main..end sub block inside a model CONTENTS stream."""
    txt = raw.decode("latin-1")
    m = re.search(r"sub main.*?end sub", txt, re.S)
    if not m:
        raise RuntimeError("no 'sub main' in CONTENTS")
    new_txt = txt[:m.start()] + script.replace("\r\n", "\n") + txt[m.end():]
    return new_txt.encode("latin-1")


# Aspen Plus stamps every user model library's root storage with this class id;
# without it, Manage Libraries > Import rejects the file as "not a valid model
# library". Read off a shipped sample (Ultrafiltration.apm) on 2026-07-11.
APM_ROOT_CLSID = "{17C95980-995F-453C-A591-E64D652FA515}"


def _write_compound(path, streams, root_clsid=APM_ROOT_CLSID):
    """Rebuild an OLE compound file from {path_tuple: bytes} via Windows
    Structured Storage (StgCreateDocfile) — handles any stream size and stamps
    the root storage CLSID Aspen requires."""
    import pythoncom
    from win32com import storagecon
    mode = (storagecon.STGM_CREATE | storagecon.STGM_READWRITE |
            storagecon.STGM_SHARE_EXCLUSIVE | storagecon.STGM_DIRECT)
    root = pythoncom.StgCreateDocfile(path, mode)
    if root_clsid:
        root.SetClass(pythoncom.MakeIID(root_clsid))
    substorages = {}

    def get_storage(parent_path):
        if not parent_path:
            return root
        key = parent_path
        if key in substorages:
            return substorages[key]
        parent = get_storage(parent_path[:-1])
        st = parent.CreateStorage(parent_path[-1],
                                  storagecon.STGM_CREATE | storagecon.STGM_READWRITE |
                                  storagecon.STGM_SHARE_EXCLUSIVE, 0, 0)
        substorages[key] = st
        return st

    for path_tuple, data in streams.items():
        parent = get_storage(tuple(path_tuple[:-1]))
        stm = parent.CreateStream(path_tuple[-1],
                                  storagecon.STGM_CREATE | storagecon.STGM_READWRITE |
                                  storagecon.STGM_SHARE_EXCLUSIVE, 0, 0)
        stm.Write(data)
        stm.Commit(0)
    for st in substorages.values():
        st.Commit(0)
    root.Commit(0)


def inject(apm_path, model_name, svg_path, out_path=None):
    script = svg_to_script(svg_path)
    streams = _read_all_streams(apm_path)
    key = (model_name, "CONTENTS")
    if key not in streams:
        raise RuntimeError("model %r not in %s (have: %s)" %
                           (model_name, apm_path, sorted(streams)))
    streams[key] = _modified_contents(streams[key], script)

    target = out_path or apm_path
    if out_path and os.path.exists(out_path):
        os.remove(out_path)
    if not out_path:
        tmp = apm_path + ".tmp"
        _write_compound(tmp, streams)
        os.replace(tmp, apm_path)
    else:
        _write_compound(out_path, streams)
    return len(script)


if __name__ == "__main__":
    apm, model, svg = sys.argv[1], sys.argv[2], sys.argv[3]
    out = sys.argv[4] if len(sys.argv) > 4 else None
    if "--print" in sys.argv:
        print(svg_to_script(svg))
    else:
        n = inject(apm, model, svg, out)
        print("injected %d-char script into %s / %s" % (n, os.path.basename(apm), model))
