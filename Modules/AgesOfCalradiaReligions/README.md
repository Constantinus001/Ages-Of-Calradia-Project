# Ages of Calradia Religions

The standalone religion-systems module for Ages of Calradia.

It loads after the core **Ages of Calradia** module. It does not own or alter
the calendar; weekday and month content remains in the core module.

## Optional War Sails shipwright v0.10.0

- Packages the port ship builder as a second assembly,
  `AgesOfCalradiaReligions.Shipwright.dll`, inside this module.
- Adds **Commission a custom Dromon** at port towns when War Sails v1.2.8 is
  active and its audited native contract is available.
- Uses the native War Sails Trade-mode port screen for upgrades, naming,
  pricing, payment, confirmation, cancellation, and fleet ownership.
- Creates the native `PortState` through `GameStateManager` and temporarily
  attaches the candidate to the port roster, matching Trade mode's actual
  runtime invariants. Cancellation and launch failure detach the candidate.
- Adds a nine-step commissioning wizard: Fore, Aft, Bow, Hull, Side, Deck,
  Sail, Roof, and Name. Each part list comes from War Sails' own current-port
  merchandise filter, preserving shipyard level and culture restrictions.
- Fits the selected pieces to a transient vanilla Dromon, then opens the native
  3D port screen for final visual inspection, price calculation, and purchase.
- Keeps War Sails optional. Without NavalDLC data, the separate sidecar safely
  hides its menu option and the religion and population systems continue.

## Character religion v0.9.0

- Gives every living hero a saved current faith, birth faith, conversion count,
  conversion date, zeal, piety, pilgrimage history, and religious legitimacy.
- Uses deterministic parent inheritance for characters born or introduced
  after campaign initialization, with culture as the fallback when family data
  is incomplete.
- Assigns campaign-start characters exactly by culture: Aserai use Mazirism;
  Empire and Vlandian characters use Valeronism; Sturgian and Nord characters
  use Veyrhold; Battanian characters use Caerwydd; and Khuzait characters use
  the Kok-Orun Way. Existing saved faiths are never overwritten, while
  later-born characters use family inheritance.
- Adds restrained monthly AI conversion pressure from crown policy, clergy
  relations, kinship between religions, spouse faith, and personal zeal. Rulers
  resist automatic conversion and all converts receive a five-year cooldown.
- Makes ruler alignment with the official faith affect clergy relations and
  calculates religious legitimacy from piety, clergy support, realm unity,
  cultural tradition, official-faith alignment, and conversion history.
- Makes governors of the local majority faith reduce provincial tension while
  unrelated governors can increase it, particularly under suppression.
- Shows ruler, governor, player, spouse, clergy officeholder, and up to six
  local notable faiths on the settlement religion page.
- Applies only restrained annual relationship changes: shared-faith spouses
  gain a small bond, highly zealous unrelated spouses may lose one relation,
  tolerant same-faith rulers gain diplomatic rapport, and suppression between
  unrelated faith realms can damage ruler relations.

## Clergy and institutions v0.8.0

- Adds saved institution tiers for every faith community in every demographic
  province: no formal institution, shrine, temple, and great sanctuary.
- Lets authorized rulers construct and upgrade the local majority institution.
  Institutions stabilize clergy strength and therefore affect conversion,
  pilgrimage activity, and religious incidents.
- Adds persistent settlement clergy offices using existing living local
  notables as officeholders, with a safe ruling-clan fallback when no notable
  is available.
- Adds clergy endowments and a capped local clergy treasury. Rulers may levy it
  once per year, exchanging revenue for lower happiness, higher tension, and
  damaged clergy relations.
- Adds realm clergy governance: Clerical Autonomy, Crown Concordat, and Crown
  Supervision. Governance changes long-term clergy relations and minority
  tension without introducing a separate deep-economy simulation.

## Religion consequences v0.7.0

- Adds saved personal piety and annual pilgrimage history.
- Allows recognized believers to undertake pilgrimages at accessible holy
  places. Restricted access raises the offering cost; closed access blocks the
  journey. Pilgrimages strengthen zeal, piety, clergy, local happiness, and
  relations while reducing tension.
- Generates restrained monthly incidents from actual local conditions:
  pilgrim markets, interfaith festivals, clerical disputes, resistance to
  suppression, and sectarian violence.
- Makes incident outcomes affect provincial happiness and religious tension,
  reports the latest result in religion and debug pages, and limits campaign
  notifications to one player-realm incident per monthly update.

## Population and opening-peace release

## Map-overlay text and coastal anchors v0.9.3

- Forces kingdom, town, friendly-army, and campaign political labels to pure
  black at runtime.
- Scopes strategic changes to `StrategicMapCanvas`; it does not edit the
  protected World Events prefab, border renderer, islands, or UI dimensions.
- Clones each instantiated text brush before setting its rendered `FontColor`,
  avoiding changes to shared UI brushes while overriding the cream glyph color.
- Campaign political-map kingdom labels use black letters with a 0.10 outline
  made from a 55%-darkened version of that kingdom's current banner color.
  Aserai and Nord labels are anchored to the owned town or castle nearest their
  geographic center, keeping their names on land instead of averaging into seas.
- Writes `[MAPTEXT]` diagnostics with separate widget-tint and rendered-brush
  verification counts. A
  failed lookup records the exact missing layer, canvas, or widget stage.

## Religion system v0.6.0

- Gives heroes a persistent personal faith, zeal score, and conversion history.
- Gives kingdoms a persistent official faith, crown policy, clergy relations,
  and population-weighted religious unity.
- Adds four ruler policies: Universal Protection, Traditional Tolerance,
  Official Supremacy, and Suppression.
- Tracks each province's real faith cohorts, clergy institution strength,
  religious tension, and monthly conversions without changing total population.
- Defines the lore-faith families and clergy traditions, including the three
  related Aserac religions: Asharim, Valeronism, and Mazirism.
- Makes Danustica the shared holy city of those three religions and adds the
  established holy places of the other living faiths. Access is saved and can
  be Open, Restricted, or Closed by a controlling ruler.
- Adds a standalone settlement page, **Religion and holy places**, for census,
  clergy, tension, holy-place access, personal conversion, festivals, official
  faith, and crown policy. It does not replace or resize World Events UI.

- Creates one demographic province for every strategic-map town and castle and
  reconciles the campaign baseline to exactly 61,000,000 people.
- Reserves 15,250,000 people for the 20 major urban regions; rural and pastoral
  weighting keeps the north substantially less dense.
- Runs births, deaths, food and war losses, migration, manpower recovery, and
  slow faith-cohort change once per Gregorian calendar month. Baseline vital
  rates are 45 births and 42 deaths per 1,000 people annually before local
  conditions, producing 0.3% baseline natural growth.
- Adds province tax and conscription controls. Tax policy modifies native town
  and village income; both policies affect population happiness.
- Limits mobilization to 10% of total population. Every recruited soldier is
  one person removed from provincial manpower. Playable party and garrison
  sizes are constrained separately by logistics and command capacity.
- Towns replenish volunteer rosters substantially faster than villages, with
  an additional population-density advantage for the 20 major urban regions.
  These recruits still consume the same finite provincial manpower pool.
- Gives towns a saved urban volunteer reserve and a batch-recruit action. Each
  recruited troop removes exactly one person from both that reserve and the
  province's available manpower; there is no hidden 1-to-25 troop conversion.
- Allocates 65% of mobilized manpower to garrisons and 35% to field parties.
- Forces every kingdom to peace for the first 20 days of a new campaign. The
  treaty is saved and does not restart when a campaign is loaded.
- Establishes stable faith identifiers for Asharim, Valeronism, Mazirism,
  Isharan Way, Kok-Orun Way, Caerwydd, Veyrhold, and the Calradic Old Faith.
- Reserves Danustica as the shared holy site of the three Aserac faiths.
- Publishes a read-only monthly strategic-map snapshot for a future separate
  World Events integration. The Religions module now supplies a transparent
  overlay with compact `P`, `R`, `POP`, and `C` buttons in the narrow strip
  above the strategic map and recolours the live atlas without changing the
  protected core DLL or prefab.
- Separates data from code so later systems can use the lore without changing
  the core module.

## Planned religion layers

1. Settlement and character faith affiliation.
2. Holy-site ownership, pilgrimage, and tolerance.
3. Conversion, clergy, festivals, and religious incidents.
4. Faith-specific legitimacy and succession rules.

## Explicit boundary

This module must not rename months or weekdays, patch the calendar UI, or move
calendar assets. Those remain the responsibility of `AgesOfCalradia`.

## Build

From this directory:

```powershell
dotnet msbuild .\AgesOfCalradiaReligions.csproj /t:Rebuild /p:Configuration=Release
```

The project writes its verified staging DLL to this module source directory's
`bin\Win64_Shipping_Client` folder. Deployment is a separate explicit step.
