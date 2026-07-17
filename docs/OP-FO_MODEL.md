# OP-FO — Forward Osmosis Model

## Physics
Osmotically driven water transport from the low-π feed into the high-π draw
(no applied pressure):

```
Jw = A · σ · (π_draw − π_feed)          [L/m2/h]
Js = B · (C_draw − C_feed)              [mol/m2/h]   (reverse draw-solute flux)
```

- `A` — water permeability (FO membranes 0.5–3 L/m²/h/bar)
- `σ` — reflection coefficient; lumps internal/external concentration
  polarization (the McCutcheon–Elimelech ICP model reduces the effective
  driving force exactly this way at fixed orientation)
- `B` — reverse solute permeability; `Js/Jw` is the standard FO selectivity metric

Driving force is evaluated at the **module average** of inlet/outlet osmotic
pressures per side; the transfer is solved by a deterministic damped fixed
point (contraction: transfer shrinks the driving force). π from van 't Hoff
`π = i·c·R·T`, or `−(RT/Vw)·ln(a_w)` when the package supplies water activity.
Reverse solute flux is distributed over the draw solutes pro rata and never
drains more than half the draw solute.

## Validity
Draw π must exceed feed π; A 0.5–3; MaxTransfer cap ≤ 99 % of feed water.

## Test anchors
- Zero transfer + warning when π_draw ≤ π_feed.
- Seawater-strength draw (~0.6 M NaCl ≈ 29 bar van 't Hoff) vs brackish feed:
  transfer > 0, draw dilutes (ratio > 1), feed concentrates (CF > 1).
- Reverse salt flux moves draw solute INTO the feed (direction check).
- Exact mass balance on every component; determinism; results == streams.

## References
- Cath, Childress & Elimelech, *J. Membr. Sci.* 281 (2006) 70–87.
- McCutcheon & Elimelech, *J. Membr. Sci.* 284 (2006) 237–247.
- Phillip, Yong & Elimelech, *Environ. Sci. Technol.* 44 (2010) 5170–5176.
