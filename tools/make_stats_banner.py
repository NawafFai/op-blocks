# Regenerates docs/media/stats.png (the README "at a glance" banner).
# Edit CARDS below when the numbers change, then:  python tools\make_stats_banner.py
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

W, H = 1170, 233
HEADER_H = 74
NAVY, BLUE = (20, 58, 99), (41, 171, 226)
BG = (240, 244, 248)
CARD_BORDER = (223, 230, 238)
VALUE_NAVY = (23, 55, 94)
GREEN = (33, 164, 68)
CAPTION = (100, 128, 156)

TITLE = "ONE PROCESS Blocks — at a glance"
SUBTITLE = "Validated CAPE-OPEN unit operations for Aspen Plus and DWSIM"
CARDS = [
    ("25", "custom blocks", VALUE_NAVY, False),
    ("5", "process families", VALUE_NAVY, False),
    ("382", "unit tests passing", GREEN, True),
    ("2", "simulator hosts", VALUE_NAVY, False),
    ("25/25", "live Aspen sweep", VALUE_NAVY, False),
    ("1e-9", "mass-balance closure", VALUE_NAVY, False),
]

FONTS = Path(r"C:\Windows\Fonts")
f_title = ImageFont.truetype(str(FONTS / "segoeuib.ttf"), 27)
f_sub = ImageFont.truetype(str(FONTS / "segoeui.ttf"), 14)
f_value = ImageFont.truetype(str(FONTS / "segoeuib.ttf"), 40)
f_caption = ImageFont.truetype(str(FONTS / "segoeui.ttf"), 14)

img = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(img)

# header: horizontal navy -> blue gradient
for x in range(W):
    t = x / (W - 1)
    c = tuple(round(NAVY[i] + (BLUE[i] - NAVY[i]) * t) for i in range(3))
    d.line([(x, 0), (x, HEADER_H)], fill=c)
d.text((24, 12), TITLE, font=f_title, fill=(255, 255, 255))
d.text((25, 48), SUBTITLE, font=f_sub, fill=(214, 236, 250))

# cards
margin, gap, top, bottom = 14, 13, HEADER_H + 14, H - 13
cw = (W - 2 * margin - 5 * gap) / 6
for i, (value, caption, color, check) in enumerate(CARDS):
    x0 = margin + i * (cw + gap)
    box = (round(x0), top, round(x0 + cw), bottom)
    d.rounded_rectangle(box, radius=8, fill=(255, 255, 255), outline=CARD_BORDER, width=1)
    cx = (box[0] + box[2]) / 2
    vb = d.textbbox((0, 0), value, font=f_value)
    vy = top + 38 if check else top + 30
    d.text((cx - (vb[2] - vb[0]) / 2, vy), value, font=f_value, fill=color)
    if check:  # hand-drawn checkmark (the glyph is absent from Segoe UI Bold)
        cy = top + 24
        d.line([(cx - 6, cy), (cx - 1, cy + 5), (cx + 7, cy - 6)], fill=GREEN, width=3, joint="curve")
    tb = d.textbbox((0, 0), caption, font=f_caption)
    d.text((cx - (tb[2] - tb[0]) / 2, bottom - 34), caption, font=f_caption, fill=CAPTION)

out = Path(__file__).resolve().parent.parent / "docs" / "media" / "stats.png"
img.save(out)
print(f"wrote {out}")
