# ADR-0021: Map the canonical layout to two bounded contexts with one Domain and two Application projects

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi
- Supersedes: ADR-0006

## Context

ADR-0006 mapped the Pivot.Framework canonical layout onto an app with no back end: it left out the Aspire AppHost and
ServiceDefaults, Dockerfiles, `IntegrationEvents` and `Persistence.EntityFrameworkCore`, because there was nothing to
orchestrate, deploy or store. With a data service (ADR-0009), a server database (ADR-0010), jobs (ADR-0013), an admin
app (ADR-0017) and a device database (ADR-0018), most of those reasons are gone. The rest of ADR-0006 still holds and is
restated below so that this ADR can replace it.

Two bounded contexts appear: *parking data* on the server and *driver assistant* on the phone. They share the engine's
value objects, which the API serialises and the parity relies on. The APK must not carry server handlers, ports or the
server's mediator scan.

Options considered:

1. One Domain project, two Application projects (phone and back end), two persistence projects.
1. One Domain and one Application project.
1. Two Domain and two Application projects.

## Decision

We take option 1. The layout becomes:

- `src/Containers/API/ParkingTracker.ApiService`, `src/Containers/Jobs/ParkingTracker.RefreshJobs` and
  `src/Containers/UI/Hosting/ParkingTracker.AdminWeb`, each with a Dockerfile.
- `src/Containers/Aspire/Hosting/ParkingTracker.AppHost` and `src/Containers/Aspire/Defaults/ParkingTracker.ServiceDefaults`,
  for local development and the server hosts only; the AppHost is never deployed.
- `src/Containers/UI/Core/ParkingTracker.Admin.ViewModels` beside the existing `ParkingTracker.Shared` and
  `ParkingTracker.ViewModels`.
- `src/Core/ParkingTracker.Domain` shared by both contexts; `src/Core/ParkingTracker.Application` for the phone;
  `src/Core/ParkingTracker.Backend.Application` for the server; `src/Core/ParkingTracker.Contracts` for the v1
  transport records.
- `src/Infrastructure/ParkingTracker.Persistence.EntityFrameworkCore` (PostgreSQL) and
  `src/Infrastructure/ParkingTracker.Persistence.Device` (SQLite) beside the existing `ParkingTracker.Infrastructure`.
- `test/ParkingTracker.Api.E2E.Tests` for the API, jobs and admin app against Testcontainers PostgreSQL.
- `deploy/` for the Compose file, the Caddy configuration and the deployment scripts.
- No `IntegrationEvents` project: nothing leaves the service as a message (ADR-0012).

Rules of ADR-0006 that stay in force:

- The MAUI host `src/Containers/UI/Hosting/ParkingTracker.App` stays thin: composition root and Android platform code,
  no screens, `net10.0-android`, and out of `ParkingTracker.slnx` because it needs the Android workload.
- Screens live in the Razor class library `ParkingTracker.Shared` and view models in `ParkingTracker.ViewModels`, so
  they test with bUnit on any operating system.
- Feature folders in every project (`Features/<Feature>/`, with `Features/Shared/`), and a test tree that mirrors
  `src/` with one test project per source project.
- `test/ParkingTracker.E2E.Tests` is the Android emulator test; `test/fixtures/` holds the recorded responses.
- SonarCloud stays open until the owner provides a project and token; the `.NET quality gate` workflow is the gate.

## Consequences

### Positive

- The APK carries no server handlers or ports, and the phone's mediator scan stays small.
- The value objects exist once, so the contract and the parity tests use the same types.
- The repository again looks like other Pivot.Framework repositories, with real hosts behind each canonical folder.

### Negative

- The APK still carries the server aggregates' types (small, no extra dependencies).
- Two Application projects depart from the single-Application canonical tree, which new contributors must learn here.
- More projects to build, reference and keep consistent.

## Alternatives considered

- **One Application project** - rejected. It puts job, admin and repository ports into the APK and into the phone's
  mediator scan.
- **Two Domain projects** - rejected. It duplicates the value objects the API serialises and the parity relies on.
