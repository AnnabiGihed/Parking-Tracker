# ADR-0009: Add a parking data service on a VPS and keep the spot decision on the phone

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi
- Supersedes: ADR-0001

## Context

ADR-0001 moved the Android app to .NET 10 MAUI Blazor Hybrid on Pivot.Framework and stated that the app has no
database, broker, cache, scheduler, API host or sign-in, so it used only `Pivot.Framework.Domain`, `Application` and
`Tools.DependencyInjection`. It also kept the hand-written HTML and CSS and rejected `Infrastructure.Persistence.Sqlite`
for local data.

What changed on 2026-11-17: the owner decided that a back-end API on an OVH VPS becomes the main provider of parking
data, refreshed by scheduled jobs per municipality, with Google sign-in, an admin web app, a local database on the
phone and Radzen screens. The "no back end, no sign-in, no database" rules and the exclusion of the other Pivot
packages no longer hold. The rest of ADR-0001 still holds and is restated below so that this ADR can replace it.

Forces:

- Honesty is the product: never FREE without confirmed data; any missing source gives CAN'T VERIFY.
- The decision engine is parity-tested on 20 recorded scenarios; any change to it risks behaviour drift.
- The card, the plate and the positions the driver checks are personal data.
- One VPS without an SLA must not make the app less available than today.

Options considered:

1. A data API: the server stores and serves the upstream data with its freshness; the phone keeps the decision.
1. A decision API: the phone sends the position and the card scope; the server returns the verdict.
1. Both: a decision API for the background parking alert only, a data API for the rest.

## Decision

We add a parking data service (an ASP.NET Core API, a Jobs host and an admin web app on one VPS, with PostgreSQL) and
keep the spot decision on the phone (option 1).

- The phone's existing ports (`IParkingBrusselsClient`, `IAddressPointsClient`, `IParkingRulesClient`,
  `IMobilityLayersClient`, the City document check) get an implementation that calls the API. The decider, the step
  runner and the view models do not change.
- The server owns the stored parking data and its freshness; the phone owns the card, places, parked car and the
  decision.
- Pivot packages beyond the three of ADR-0001 are now allowed where a later ADR names them (ADR-0010 to ADR-0018).

Rules of ADR-0001 that stay in force:

- The Android app is a .NET 10 MAUI Blazor Hybrid app on Pivot.Framework, Android only, installed as an update of
  `be.parkingtracker.app` with the same release key and a rising versionCode.
- The decision engine, house-number rules, sector model, geometry, zone types, card scope, paid hours, Belgian
  holidays, parking detection, card-text parsing and the live-data clients are C#; Leaflet stays in JavaScript behind
  `wwwroot/js/map.js`.
- Reference data is JSON embedded in `ParkingTracker.Infrastructure`; the Python tools stay the source of truth.
- Behaviour parity is tested on recorded fixtures; xUnit, FluentAssertions and NSubstitute are the test stack.
- `.github/workflows/android.yml` keeps its name, restores Pivot packages with the workflow token, tests a Release build
  with test hooks on an emulator and releases from `main` only.
- Release builds use trimming; reflection-based registration stays out of full trimming.

The HTML and CSS rule of ADR-0001 is replaced by ADR-0020 (Radzen); the project layout is replaced by ADR-0021.

## Consequences

### Positive

- The parity proof stays intact by construction: only the adapters behind the ports change.
- The card and the precise positions never reach the server; the server sees bounding boxes only.
- The background parking alert keeps working as today.
- Server failures cannot produce a wrong answer: they map to a failed source (ADR-0011, ADR-0024).

### Negative

- Several API requests per decision until a bundle endpoint exists; each costs a mobile round trip.
- Engine changes still need an app release.
- The API contract mirrors the domain value objects, so a value object change is a contract change.
- A server, a database and jobs to operate, patch and back up, where today there is nothing.

## Alternatives considered

- **Decision API** - rejected. It sends the card scope and every checked position of an identified user to the
  server, which would become a location history; every check would depend on the VPS, including the background alert;
  and the parity suite would have to run against a deployed service.
- **Both a data and a decision API** - rejected. It carries the costs of both and two decision paths that must stay
  equal.
- **No back end (ADR-0001 unchanged)** - rejected. It does not meet the owner's requirement of an API as the main data
  provider.
