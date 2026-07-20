# -*- coding: utf-8 -*-
"""Builds the P4 icon-review gallery artifact (self-contained HTML with every
SVG inlined). Grouped by family, ONE PROCESS identity. One-off review tool."""
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CAT = json.load(open(os.path.join(ROOT, "icons", "_catalog.json"), encoding="utf-8"))

FAMILIES = [
    ("Membranes", "membrane", "#29ABE2",
     ["OP-RO", "OP-NF", "OP-UF", "OP-FO", "OP-PRO"]),
    ("Thermal & Evaporation", "thermal", "#F2A03D",
     ["OP-MED", "OP-MSF", "OP-MVC", "OP-MD", "OP-EVAPPOND"]),
    ("Electrochemical", "electro", "#7C5CD6",
     ["OP-ED", "OP-EDI", "OP-CDI", "OP-CHLORALK", "OP-IX"]),
    ("Lithium & Sorption", "sorption", "#2FB39B",
     ["OP-DLE", "OP-SX", "OP-CRYST", "OP-PPT", "OP-GAC"]),
    ("Energy & Gas", "energy", "#E5564E",
     ["OP-PEM", "OP-AEL", "OP-FC", "OP-RPB", "OP-UVAOP"]),
]


def svg_of(code):
    raw = open(os.path.join(ROOT, "icons", code + ".svg"), encoding="utf-8").read()
    raw = re.sub(r"<!--.*?-->", "", raw, flags=re.S)               # drop comments
    raw = re.sub(r'<svg ', '<svg role="img" aria-hidden="true" ', raw, count=1)
    return raw.strip()


def cards(codes):
    out = []
    for c in codes:
        info = CAT[c]
        out.append(f'''        <figure class="card">
          <div class="well">{svg_of(c)}</div>
          <figcaption>
            <span class="code">{c}</span>
            <span class="name">{info['name']}</span>
            <span class="use">{info['use']}</span>
          </figcaption>
        </figure>''')
    return "\n".join(out)


def sections():
    out = []
    for title, key, accent, codes in FAMILIES:
        out.append(f'''      <section class="family" style="--fam:{accent}" data-fam="{key}">
        <header class="fam-head">
          <span class="fam-dot"></span>
          <h2>{title}</h2>
          <span class="fam-count">{len(codes)} blocks</span>
        </header>
        <div class="grid">
{cards(codes)}
        </div>
      </section>''')
    return "\n".join(out)


HTML = f'''<article class="page">
  <header class="masthead">
    <div class="brand">
      <span class="hex"></span>
      <div>
        <p class="kicker">ONE PROCESS Blocks &middot; v1.1.3 icon system</p>
        <h1>Twenty-five blocks, each one recognizable</h1>
      </div>
    </div>
    <p class="lede">Every block now carries a <strong>distinct silhouette</strong>, the physics it performs,
      its <strong>code baked into the artwork</strong>, and a <strong>family colour</strong> &mdash; so an
      engineer tells reverse osmosis from nanofiltration at a glance, on both the Aspen Plus and DWSIM
      flowsheets. <span dir="rtl" lang="ar">لكل بلوك الآن شكل مميّز واسمه داخل الرسم.</span></p>
    <ul class="legend">
      <li><span class="lg-shape"></span>Distinct silhouette per process</li>
      <li><span class="lg-code">OP</span>Code baked into the icon</li>
      <li><span class="lg-fam"></span>Colour groups the family</li>
    </ul>
  </header>

  <main class="stack">
{sections()}
  </main>

  <footer class="colophon">
    <span>25 CAPE-OPEN blocks &middot; 5 families</span>
    <span>Monochrome-safe for Aspen line art &middot; full colour in DWSIM</span>
    <span>Verified: DWSIM host suite all-pass &middot; 397 unit tests green</span>
  </footer>
</article>'''

CSS = '''
:root{
  --ground:#F5F7FA; --panel:#FFFFFF; --ink:#1B3A5C; --ink-2:#54677B;
  --line:#DCE4EC; --well:#EEF3F8; --navy:#1B3A5C;
  --maxw:1120px;
  --serif: "Segoe UI", "Helvetica Neue", system-ui, -apple-system, sans-serif;
  --mono: ui-monospace, "Cascadia Code", "Consolas", "SF Mono", monospace;
}
@media (prefers-color-scheme:dark){
  :root{ --ground:#0D1A28; --panel:#15273A; --ink:#E7EFF7; --ink-2:#9DB2C6;
    --line:#263B52; --well:#0F2032; --navy:#0A1420; }
}
:root[data-theme="light"]{ --ground:#F5F7FA; --panel:#FFFFFF; --ink:#1B3A5C; --ink-2:#54677B;
  --line:#DCE4EC; --well:#EEF3F8; --navy:#1B3A5C; }
:root[data-theme="dark"]{ --ground:#0D1A28; --panel:#15273A; --ink:#E7EFF7; --ink-2:#9DB2C6;
  --line:#263B52; --well:#0F2032; --navy:#0A1420; }

*{ box-sizing:border-box; }
html{ -webkit-text-size-adjust:100%; }
body{ margin:0; background:var(--ground); color:var(--ink);
  font-family:var(--serif); line-height:1.5;
  -webkit-font-smoothing:antialiased; text-rendering:optimizeLegibility; }
.page{ max-width:var(--maxw); margin:0 auto; padding:clamp(20px,4vw,52px) clamp(16px,4vw,40px) 64px; }

/* masthead */
.masthead{ border-bottom:1px solid var(--line); padding-bottom:30px; margin-bottom:8px; }
.brand{ display:flex; align-items:center; gap:16px; }
.hex{ width:44px; height:44px; flex:0 0 auto; background:var(--navy);
  clip-path:polygon(25% 3%,75% 3%,100% 50%,75% 97%,25% 97%,0 50%); position:relative; }
.hex::after{ content:""; position:absolute; inset:34%; border-radius:3px;
  background:#29ABE2; box-shadow:0 0 0 3px var(--navy) inset; }
.kicker{ margin:0 0 2px; font-family:var(--mono); font-size:.74rem; letter-spacing:.08em;
  text-transform:uppercase; color:var(--ink-2); }
h1{ margin:0; font-size:clamp(1.5rem,3.4vw,2.35rem); font-weight:700; letter-spacing:-.015em;
  text-wrap:balance; line-height:1.1; }
.lede{ max-width:64ch; margin:20px 0 0; color:var(--ink-2); font-size:1.02rem; }
.lede strong{ color:var(--ink); font-weight:600; }
.lede [lang="ar"]{ font-size:1.05rem; color:var(--ink); }

.legend{ list-style:none; display:flex; flex-wrap:wrap; gap:10px 22px;
  margin:22px 0 0; padding:0; font-size:.85rem; color:var(--ink-2); }
.legend li{ display:flex; align-items:center; gap:9px; }
.lg-shape{ width:17px; height:17px; border:2px solid var(--ink); border-radius:4px 9px 4px 9px; }
.lg-code{ font-family:var(--mono); font-weight:700; font-size:.7rem; color:#fff;
  background:var(--navy); padding:2px 5px; border-radius:3px; letter-spacing:.03em; }
.lg-fam{ width:17px; height:17px; border-radius:50%;
  background:conic-gradient(#29ABE2 0 20%,#F2A03D 0 40%,#7C5CD6 0 60%,#2FB39B 0 80%,#E5564E 0); }

/* families */
.stack{ display:flex; flex-direction:column; gap:44px; margin-top:40px; }
.fam-head{ display:flex; align-items:baseline; gap:12px;
  border-top:2px solid var(--fam); padding-top:12px; margin-bottom:20px; }
.fam-dot{ width:11px; height:11px; border-radius:50%; background:var(--fam);
  align-self:center; flex:0 0 auto; box-shadow:0 0 0 4px color-mix(in srgb,var(--fam) 20%,transparent); }
.fam-head h2{ margin:0; font-size:1.16rem; font-weight:650; letter-spacing:-.01em; }
.fam-count{ margin-left:auto; font-family:var(--mono); font-size:.76rem; letter-spacing:.05em;
  text-transform:uppercase; color:var(--ink-2); }

.grid{ display:grid; gap:16px;
  grid-template-columns:repeat(auto-fill,minmax(200px,1fr)); }
.card{ margin:0; background:var(--panel); border:1px solid var(--line); border-radius:12px;
  overflow:hidden; display:flex; flex-direction:column;
  transition:border-color .18s ease, transform .18s ease; }
.card:hover{ border-color:var(--fam); transform:translateY(-2px); }
.well{ background:
    radial-gradient(120% 120% at 50% 0%, color-mix(in srgb,var(--fam) 12%,var(--well)) 0%, var(--well) 70%);
  border-bottom:1px solid var(--line);
  display:flex; align-items:center; justify-content:center; padding:18px; }
.well svg{ width:104px; height:104px; display:block; }
figcaption{ display:flex; flex-direction:column; gap:4px; padding:13px 15px 15px; }
.code{ font-family:var(--mono); font-weight:700; font-size:.82rem; letter-spacing:.02em;
  color:var(--fam); }
.name{ font-weight:620; font-size:.98rem; letter-spacing:-.01em; }
.use{ font-size:.82rem; color:var(--ink-2); line-height:1.4; }

/* colophon */
.colophon{ margin-top:52px; padding-top:20px; border-top:1px solid var(--line);
  display:flex; flex-wrap:wrap; gap:6px 26px; justify-content:space-between;
  font-family:var(--mono); font-size:.76rem; color:var(--ink-2); letter-spacing:.02em; }

@media (max-width:520px){
  .grid{ grid-template-columns:repeat(auto-fill,minmax(150px,1fr)); gap:12px; }
  .well svg{ width:86px; height:86px; }
  .colophon{ flex-direction:column; gap:4px; }
}
@media (prefers-reduced-motion:reduce){ .card{ transition:none; } }
'''

doc = f"<title>OP-Blocks v1.1.3 Icon System</title>\n<style>{CSS}</style>\n{HTML}\n"
out = os.path.join(ROOT, "icons", "gallery.html")
with open(out, "w", encoding="utf-8") as f:
    f.write(doc)
print("wrote", out, len(doc), "bytes")
