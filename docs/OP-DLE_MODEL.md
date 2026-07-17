# OP-DLE — Direct Lithium Extraction Model

## Physics
Cycle-averaged selective Li⁺ sorption column:

```
Langmuir:   q* = q_max · K · C / (1 + K·C)        [mg Li/g]
LDF:        q(t) = q* · (1 − e^(−k_LDF · t_cycle))     (Glueckauf)
capture:    N_Li = q · m_bed / MW_Li / t_cycle,  capped by the Li fed
Mg co-sorption: N_Mg = Mg_feed · (Li_recovery / S_MgLi)
```

Exact anchors pinned by tests: half-loading `q* = q_max/2` at `C = 1/K`;
`q* → q_max` as C → ∞; the LDF factor in closed form.

## Validity
LMO/LLTO-class sorbents: q_max 5–40 mg/g, S_MgLi 20–500, cycles 1–24 h.

## Test anchors
- Langmuir half-loading exact; saturation limit; LDF approach exact.
- Sorbent-limited vs feed-limited regimes with warnings.
- Mg co-capture at recovery/S; exact 4-stream mass balance; determinism;
  results == streams.

## References
- Langmuir, *J. Am. Chem. Soc.* 40 (1918) 1361–1403.
- Glueckauf, *Trans. Faraday Soc.* 51 (1955) 1540–1551.
- Battistel et al., *Adv. Mater.* 32 (2020) 1905440.
