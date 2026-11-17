# ADR-0006: Map the Pivot.Framework canonical layout onto an on-device mobile client

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The Pivot.Framework canonical solution layout (`dotnet-solution-scaffolding`) is written for services: API hosts, an
Aspire AppHost with ServiceDefaults, Dockerfiles, integration events and a persistence project on Entity Framework
Core. Parking Tracker is an Android app that decides on the phone and calls public HTTPS services. It has no back end,
publishes no events, owns no database and ships as an APK (ADR-0001, ADR-0004).

Forces:

- The house rules ask for one layout in every repository, with feature folders in every project and a test tree that
  mirrors `src/`.
- Several parts of the canonical layout have nothing to hold here, and an empty project only costs build time and
  confuses readers.
- The screens must be testable on any operating system. The MAUI host only builds with the Android workload, which
  exists in CI but not on every developer PC (`docs/architecture.md` §7).
- The clean-code-static-analysis rules also ask for a SonarCloud quality gate (R12). That needs a SonarCloud project and
  token owned by the repository owner.

Options:

1. Scaffold the full canonical layout, including empty or unused projects.
2. Keep the layers that carry code, adapt the UI projects to a MAUI host, and leave out what has nothing to hold.
3. Keep the earlier flat layout (`src/Core`, `src/Infrastructure`, `src/Containers/Mobile`, one test project each).

## Decision

We take option 2. The canonical layout applies as follows.

Kept:

- `src/Core/ParkingTracker.Domain` and `src/Core/ParkingTracker.Application`: pure rules, queries, handlers, validators
  and ports.
- `src/Infrastructure/ParkingTracker.Infrastructure`: the HTTP clients of the live sources and the embedded reference
  data.
- `src/Containers/UI/Core/ParkingTracker.Shared` (planned, added in stage 5): the Razor pages and components, as a
  Razor class library so that they can be tested with bUnit on any operating system.
- `src/Containers/UI/Core/ParkingTracker.ViewModels` (planned, added in stage 5): the view models, per feature.
- `src/Containers/UI/Hosting/ParkingTracker.App` (planned, added in stage 5): a thin .NET MAUI Blazor Hybrid host for
  Android. It holds the composition root and the Android platform code (location service, notifications, permissions,
  OCR, data migration) and no screens. It overrides the target framework from `Directory.Build.props`
  (`net10.0-android`).
- Feature folders in every project: `Features/<Feature>/` with `Features/Shared/` for what several features use. In
  Domain each feature has `ValueObjects/` and `Services/` (and `Errors/` when needed); in Application it has `Queries/`,
  `Services/` and `Abstractions/` (ports).
- A test tree that mirrors `src/` (`test/Core/...`, `test/Infrastructure/...`), with the same `Features/<Feature>/`
  folders as the code under test, and one test project per source project.
- `test/ParkingTracker.E2E.Tests` (planned, added in stage 5): the Android emulator test. It replaces the API
  end-to-end test of a service and keeps the role of `tools/emulator-test.mjs`.
- `test/fixtures/`: the recorded live responses, which the parity tests and `tools/parity/record.mjs` use. It is data,
  not a project.

Not applicable, and why:

- Aspire AppHost and ServiceDefaults: there are no services to orchestrate and no dashboard to open.
- Dockerfile: an APK is not a container image; the deployable is the signed APK from the Android workflow.
- `IntegrationEvents`: the app publishes no events to other systems.
- `Persistence.EntityFrameworkCore`: there is no database. The card and the settings live in device storage (MAUI
  `SecureStorage` and `Preferences`, behind the `ISettingsStore` port).
- API reference page (Scalar or Swagger UI): the app exposes no API.

Open: SonarCloud (R12) is not set up. It needs the owner's SonarCloud project and token; until then the quality gate is
the `.NET quality gate` workflow (restore, format check, Release build with warnings as errors and the analyzers, tests).

This ADR refines the project layout described in ADR-0001; the framework, the platform and the other decisions of
ADR-0001 stay as they are.

## Consequences

### Positive

- The repository looks like every other Pivot.Framework repository, so tooling and reviewers find things where they
  expect them.
- The screens and the view models can be tested on any operating system; only the thin host needs the Android workload.
- No empty projects for services the app does not have.

### Negative

- The layout departs from the canonical one in the places listed above, so each deviation has to be explained to new
  contributors; this ADR is that explanation.
- The UI is split in three projects before the app has many screens, which adds references to maintain.
- Feature folders make the paths longer, and a type has to be moved when a second feature starts to use it.

## Alternatives considered

- **Scaffold the full canonical layout** - rejected. It adds projects that would stay empty and imply a back end that
  does not exist.
- **Keep the flat layout** - rejected. It breaks the feature-first rule, gives the screens no testable home and keeps
  the Android workload in the way of every UI test.
