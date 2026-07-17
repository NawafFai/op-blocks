# OP-PPT — Chemical Precipitation Model

## Physics
Stoichiometric, **reagent-limited** precipitation:

```
removable_by_reagent = reagent_fed / dose        (dose = mol reagent / mol target)
removed = min(target · removal_eff, removable_by_reagent)
```

Consumed reagent (`removed · dose`) reports to the sludge with the
precipitate; unreacted excess stays dissolved in the treated water; the
reagent's carrier water joins the treated stream — the 4-stream mass balance
is exact. pH is an advisory input (hydroxide softening optimum 10.3–11).

## Test anchors
- Reagent-limited case exact: removed = reagent/dose, with warning.
- Ample-reagent case: removed = eff·target; consumed = removed·dose exact.
- Exact 4-stream mass balance; determinism; results == streams.

## References
- Metcalf & Eddy / AECOM, *Wastewater Engineering*, 5th ed. (2014), ch. 6.
- Crittenden et al. (MWH), *Water Treatment*, 3rd ed. (2012), ch. 13.
