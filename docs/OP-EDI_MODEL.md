# OP-EDI — Electrodeionization Model

## Physics
EDI = electrodialysis + mixed-bed resin (continuous polishing to ultrapure).
Ion transport remains **Faradaic**:

```
N_max = η_i · I · N_cp / (z · F)      I = V / R_stack
removal_achieved = min(target, N_max / ion_load)
```

Excess current beyond the ion load splits water, continuously regenerating
the resin (the Ganzi mechanism) — reported as the water-splitting fraction.
`SEC = V·I / Q_dilute`.

**Golden-rule fix**: `CellPairs` was an IntegerParameter (blanks Aspen's
grid) → REAL integer code, as are `IonValence`.

## Validity
Polishing duty after RO (feed < ~50 ppm); removal 90–99.99 %; water-splitting
fraction near 1 indicates an oversized stack.

## Test anchors
- Faraday exact: `I = F` A, 1 cell pair, η=100 %, z=1 → capacity exactly 1 mol/s.
- Current-limited case: achieved removal = capacity/load with warning.
- Ample-current case: achieved = target (99 %).
- Exact 4-stream mass balance; determinism; results == streams.

## References
- Ganzi, Egozy, Giuffrida & Jha, *Ultrapure Water* 4 (1987) 43–50.
- Wood, Gifford, Arba & Shaw, *Desalination* 250 (2010) 973–976.
- Strathmann, *Ion-Exchange Membrane Separation Processes* (2004), ch. 6.
