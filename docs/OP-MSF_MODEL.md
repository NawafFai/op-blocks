# OP-MSF — Multi-Stage Flash Model

## Physics
Once-through MSF shortcut (El-Dessouky & Ettouney ch. 6). Heated brine
flashes through N stages of decreasing pressure over the flash range
`ΔT_range = TBT − T_last`:

```
y        = cp · ΔT_stage / λ            (flash fraction per stage)
D        = M_f · [1 − (1 − y)^N]        (distillate)
Q_heater = M_f · cp · (ΔT_stage + ΔT_loss)     (BPE + NEA losses)
PR       = D · λ / Q_heater             (performance ratio ≈ GOR)
```

MSF is inherently **low-recovery**: recovery ≈ cp·ΔT_range/λ ≈ 8–12 % per
pass (plants recirculate brine) — the block reports this honestly and warns.
Distillate is pure water (salts non-volatile).

## Validity
N 10–40, TBT 90–112 °C (antiscalant limit), T_last 30–45 °C, ΔT_loss 1.5–3 K.
24 stages at defaults → PR ≈ 8–12, the published band for large MSF plants.

## Test anchors
- Recovery = 1−(1−y)^N exact against hand evaluation; ≈ cp·ΔT_range/λ to first order.
- PR in 8–13 at defaults (24 stages, TBT 110, loss 2.5 K).
- Distillate salt-free; TBT > 112 °C warns; low-recovery advisory always present.
- Exact mass balance; determinism; results == streams.

## References
- El-Dessouky & Ettouney, *Fundamentals of Salt Water Desalination* (2002), ch. 6.
- Khawaji, Kutubkhanah & Wie, *Desalination* 221 (2008) 47–69.
