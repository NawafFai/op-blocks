# OP-PRO — Pressure-Retarded Osmosis Model

## Physics
Osmotic power: water permeates from the low-salinity feed into the
**pressurised** high-salinity draw against the applied pressure `dP`, and the
permeate volume is expanded through a hydro-turbine at `dP`:

```
Jw = A · (σ·Δπ − dP)                    [L/m2/h]
W  = Jw · dP                            [W/m2]  (power density)
   = A · (σ·Δπ − dP) · dP  →  max at dP* = σ·Δπ/2,  Wmax = A(σ·Δπ)²/4
```

The `dP* = Δπ/2` optimum is the classical PRO result (Loeb 1976; Achilli &
Childress 2010) and is pinned exactly by a unit test. Δπ is evaluated at the
module average of inlet/outlet osmotic pressures per side (deterministic
damped fixed point). Gross power `= dP · Q_permeate`; net power applies the
turbine/generator efficiency. The draw outlet leaves at `P_draw,in + dP`.

## Validity
River/sea pairs Δπ ≈ 25–30 bar → Wmax ≈ 4–6 W/m² at A ≈ 1 L/m²/h/bar
(the published PRO viability band). dP must stay below σ·Δπ.

## Test anchors
- Power density at `dP = σ·Δπ/2` exceeds any other tested dP (exact peak).
- `W = A(σΔπ − dP)·dP` formula reproduced by the block within 0.1 %.
- No power + warning when `dP ≥ σ·Δπ`.
- Draw outlet pressurised by exactly dP; exact mass balance; determinism;
  results == streams.

## References
- Loeb, *J. Membr. Sci.* 1 (1976) 49–63.
- Achilli & Childress, *Desalination* 261 (2010) 205–211.
- Straub, Deshmukh & Elimelech, *Energy Environ. Sci.* 9 (2016) 31–48.
