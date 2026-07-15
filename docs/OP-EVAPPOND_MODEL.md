# OP-EVAPPOND — Model & References

Solar evaporation pond block for ONE PROCESS Blocks (CAPE-OPEN + DWSIM). The
equations below are implemented once in `OPBlocks.Core.EvapPondModel` (the single
source of truth shared by the block and its validation tests) and are also printed
in the block's own report under **"Model & References"**, so they travel with every
simulation.

Stream thermodynamics — temperature, pressure, densities, molecular weights — come
from the **user-selected Property Package**. The only block-specific closure is the
ambient water saturation vapour pressure (Antoine), used for the atmosphere-side
driving force, not for stream phase equilibria. This is a third, distinct physics
family (mass transfer to the atmosphere) proving the factory beyond pressure- and
current-driven blocks.

## Ports
- **BrineFeed** (inlet).
- **Concentrate** (outlet) — the concentrated brine remaining in the pond.
- **Vapor** (outlet) — evaporated water leaving to the atmosphere.

## Parameters (inputs)
| Name | Default | Unit | Meaning |
|---|---|---|---|
| Area | 10000 | m² | Pond surface area |
| Depth | 0.5 | m | Pond depth (for residence time) |
| Irradiance | 600 | W/m² | Solar irradiance |
| AirTemp | 30 | °C | Ambient air temperature |
| RH | 40 | % | Relative humidity |
| WindSpeed | 3 | m/s | Wind speed |
| WaterActivity | 0.98 | — | Surface water activity of the brine (salinity reduction; 1 = fresh) |
| CoeffA | 1.2×10⁻⁸ | kg·m⁻²·s⁻¹·Pa⁻¹ | Dalton coefficient a (still-air) |
| CoeffB | 2.5×10⁻⁹ | kg·m⁻²·s⁻¹·Pa⁻¹/(m/s) | Dalton wind coefficient b |
| SolarHeating | 0.012 | °C/(W/m²) | Surface warming above air per unit irradiance |

Every parameter is a `RealParameter` (the only type Aspen's grid renders).

## Model

**Evaporation — Dalton / aerodynamic mass-transfer law:**

    E = (a + b·u) · (e_s − e_a)          [kg·m⁻²·s⁻¹]

- `e_s = a_w · Psat(T_surf)` — brine surface vapour pressure. The brine water
  activity `a_w < 1` **lowers** the surface vapour pressure (the salinity effect:
  a saturated brine evaporates far slower than fresh water).
- `e_a = RH · Psat(T_air)` — ambient vapour pressure.
- `u` = wind speed; `a`, `b` = empirical Dalton coefficients (site-calibratable).
- `Psat` = Antoine water correlation (`ProcessOps.PsatWaterPa`).

**Surface temperature** — lumped solar-heating closure:

    T_surf = T_air + SolarHeating · Irradiance      (clamped 0–95 °C)

**Concentration** — only water evaporates; salts stay in the pond:

    evaporated water   = E · Area / M_water   (capped at the feed water)
    concentration CF   = feed water / (feed water − evaporated water)
    evap flux [mm/day] = E · 86400             (1 kg·m⁻² water = 1 mm depth)
    residence [day]    = Area · Depth / Q_feed

The material balance closes exactly: feed = concentrate + vapour, component-wise;
the vapour stream is pure water.

## Engineering advisories (non-blocking warnings)
- no evaporation (ambient vapour pressure ≥ surface — humid/cool, or a very saline
  low-activity brine);
- feed-limited (climate flux × area exceeds the feed — the pond is a batch
  evaporator, not a steady concentrator);
- high concentration factor (>5× — halite/gypsum saturation, add solids handling);
- property-package fallbacks used (missing MW/density).

## Neglected effects (lumped one-node model, v1)
A full surface energy balance (net radiation, sensible heat, ground conduction,
back-radiation), seepage, rainfall, the temperature and salinity dependence of
`a_w`, and stratification are not resolved. `a`, `b` and `SolarHeating` are lumped
empirical constants to be calibrated to the site's measured pan/pond data.

## References
1. H. L. Penman, "Natural evaporation from open water, bare soil and grass,"
   *Proc. R. Soc. Lond. A* **193** (1948) 120–145.
2. E. Sartori, "A critical review on equations employed for the calculation of the
   evaporation rate from free water surfaces," *Solar Energy* **68** (2000) 77–89.
3. Y. A. Salhotra, E. E. Adams, D. R. F. Harleman, "Effect of salinity and ionic
   composition on evaporation," *Water Resour. Res.* **21** (1985) 1336–1344.
4. S. Al-Shammiri, "Evaporation rate as a function of water salinity,"
   *Desalination* **150** (2002) 189–203.

## Validation status
Physics is pinned by `EvapPondCapeOpenValidationTests` (both Thermo 1.0 and 1.1
mock backends). The exact anchor is the **Antoine saturation vapour pressure**:
Psat(100 °C) ≈ 101.3 kPa (1 atm) and Psat(25 °C) ≈ 3.17 kPa (textbook), both to
within ~1%. With arid defaults the evaporation is ~4–12 mm/day (realistic pond
range); a lower brine water activity reduces evaporation (Salhotra/Al-Shammiri);
wind increases it; the concentration factor > 1 with exact salt conservation; 20
consecutive runs identical to 1e-8; results equal the outlet streams. **Live Aspen
V14 GUI acceptance is pending on the owner's machine.**
