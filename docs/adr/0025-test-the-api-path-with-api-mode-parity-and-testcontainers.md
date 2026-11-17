# ADR-0025: Test the API path with API-mode parity and Testcontainers PostgreSQL, without in-memory providers

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The 20 recorded parity scenarios prove that the C# engine gives the same answers as the Capacitor app on recorded
upstream responses. With the data service (ADR-0009), the same responses travel through the jobs' parsers, a bulk
`COPY`, PostGIS spatial queries, the API's JSON and the phone's adapter before the engine sees them. Any of those steps
can change a coordinate, drop a feature or reorder results. The spatial semantics (`ST_Intersects` against WFS `bbox`,
ADR-0010) exist only in PostGIS. The authentication path (ADR-0014) and the admin hardening (ADR-0017) are
security-critical. The existing test stack is xUnit, FluentAssertions, NSubstitute, bUnit and `FakeTimeProvider`.

Options considered:

1. API-mode parity and persistence tests against a real PostGIS container (Testcontainers), with a test issuer instead
   of Google.
1. EF Core's in-memory or SQLite provider for server tests, and mocked repositories for the rest.
1. Tests against a shared, long-lived test database or the VPS.

## Decision

We test the server path against real PostgreSQL with PostGIS through Testcontainers (option 1).

- **API-mode parity.** `test/ParkingTracker.Api.E2E.Tests` loads the recorded fixtures through the jobs' parsers and
  `COPY` into a `postgis/postgis` container, serves them through the API in a `WebApplicationFactory`, and runs the 20
  scenarios through the phone's API adapter. Verdicts must be equal; texts must be equal except the source name in
  failure messages.
- **Persistence.** Migrations apply on an empty database; the spatial query returns what WFS `bbox` returned for the
  fixtures; staged and superseded rows are never visible; database roles cannot exceed their rights.
- **Jobs.** Recorded WFS pages through a stub `HttpMessageHandler` (paging, empty answer, HTTP 500, timeout, malformed
  JSON), with the database in a container.
- **Authentication.** A test issuer with its own signing key and key set replaces Google through configuration; tests
  cover a valid exchange and every refusal (wrong audience, expired, replayed nonce, unsigned), refresh rotation, reuse
  revocation, sign-out and erasure.
- **Hardening.** `401`, `403`, `429` with `Retry-After`, Scalar `404` in Production, security headers and the
  allow-list removal taking effect on the next request.
- **Device.** The device database is tested on a real SQLite file. The emulator test uses a stubbed Google sign-in in
  the test-hooks build and a fixture-backed fake API for the API-mode scenario and the failure paths.
- In-memory providers are not used for persistence tests. Domain and application tests keep NSubstitute and
  `FakeTimeProvider`.
- Testcontainers is the one new test tool. Tests that need Docker run on a PC with Docker and in CI.

## Consequences

### Positive

- Proof that moving data through the server changes no verdict.
- Spatial and role behaviour proven on the real engine, not on a provider with different semantics.
- Security refusals are tested, not assumed.

### Negative

- Tests that need Docker are slower and do not run on a PC without Docker.
- A PostGIS image to pull in CI, and a version to keep in step with production.
- The test issuer is test-only configuration that must never be enabled in Production; a test guards it.

## Alternatives considered

- **In-memory or SQLite providers** - rejected. They have no PostGIS, different query semantics and no roles, so they
  would prove nothing about the parts most likely to break.
- **Shared test database or the VPS** - rejected. Tests would depend on shared state and on the network, and could
  touch real data.
