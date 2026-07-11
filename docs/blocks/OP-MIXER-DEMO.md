# OP-MIXER-DEMO — Demo Mixer (M0 skeleton)

The M0 skeleton block (spec §11). Its purpose is to prove the full pipeline —
registration, palette visibility, drag-drop, ports, parameters, Edit GUI,
delegated thermodynamics, report, and save/reopen persistence — on Aspen Plus V14
and DWSIM, **before** any real physics is written.

## Ports

| Port | Direction | Type |
|---|---|---|
| `Inlet1` | inlet | material |
| `Inlet2` | inlet | material |
| `Outlet` | outlet | material |

## Parameters

| Name | Type | Default | Meaning |
|---|---|---|---|
| `PressureSpec` | option {`Minimum inlet`, `Specified`} | `Minimum inlet` | how outlet pressure is set |
| `SpecifiedPressure` | real [Pa] | 101325 | outlet pressure when `PressureSpec = Specified` |

## Model

Adiabatic two-stream mixer:

- **Mass balance:** per-component outlet mole flow = Σ inlet mole flows.
- **Energy balance (spec §5 rule 1):** Q = 0, so total enthalpy flow is conserved.
  The outlet molar enthalpy target is `Σ(hᵢ·Fᵢ) / ΣFᵢ`, where the molar enthalpies
  `hᵢ` come from the **host property package** — nothing is hardcoded (R4).
- **Pressure:** minimum of the inlet pressures, or the specified value.
- The outlet is set in Aspen's required order (composition → pressure → enthalpy)
  and closed with a single **PH flash** through the host material object
  (spec §5 rule 2), all inside `ThermoProxy`.

## Robustness (spec §8)

- Unconnected ports / zero total flow → a clear `ECapeUser` message, never a crash.
- Any unexpected error → logged to `%LOCALAPPDATA%\OPBlocks\logs\` and returned as
  a `CapeUnknownException` with the log path.
- Parameters survive save → close → reopen via `IPersistStream(Init)` (verified by
  `PersistenceTests`).

## Validation (spec §9)

- Layer 1 (math): `tests/UnitTests/MixerMathTests.cs` — flow summation, enthalpy
  balance, pressure rules, degenerate cases.
- Persistence: `tests/UnitTests/PersistenceTests.cs` — lossless parameter
  round-trip through an in-memory `IStream`.
- Layer 2 (thermo consistency in COCO) and host smoke tests: to run once the block
  is registered.
