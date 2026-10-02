# AOC Systems feature source

This directory contains four core feature-source projects composed into two
runtime modules, both loaded after **AOC CORE**:

- `AgesOfCalradiaRefuges` — player camps and refuges.
- `AgesOfCalradiaLogistics` — supplies and baggage trains.
- `AgesOfCalradiaReligions` — religions and population simulation.
- `AgesOfCalradiaSuccession` — hereditary law and succession crises.

`AgesOfCalradiaSystemsLR` composes Logistics and Refuges as
**AOC SYSTEMS L & R**. `AgesOfCalradiaSystemsRS` composes Religions and
Succession as **AOC SYSTEMS R & S**. The feature projects retain their assembly
names, namespaces, and save keys; their standalone manifests remain only as
legacy source contracts and are not copied into either player package.

The Island Exclusion and Political Settings Bridge source remain under
`Builds/` because they are built as embedded optional submodules declared by
the base module manifest.

`CalradiaCampaignClock` is different from the composed systems above. It is an
independent, gameplay-neutral module containing only the numeric campaign-map
clock and its refresh/layout fixes. It neither depends on AOC CORE nor ships a
replacement MapBar prefab; UIExtenderEx adds its single widget at runtime.
