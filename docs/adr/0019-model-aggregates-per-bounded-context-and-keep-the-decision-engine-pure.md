# ADR-0019: Model aggregates per bounded context and keep the decision engine as value objects and pure services

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The owner asks for a correct domain model (aggregates, entities, value objects, domain events) and a migration that
keeps the parity-tested logic. Today the Domain project holds the decision engine: value objects (`ZoneSurface`,
`ParkingRule`, `AddressPoint`, `SpotDecision` and others) and stateless services (`SpotDecider`, `AreaDecider`,
`PaidHours` and others), tested against 20 recorded parity scenarios. There are no aggregates, because nothing had an
identity or a lifecycle that the app owned. The back end (ADR-0009) and the local database (ADR-0018) add things that
do.

Pivot's patterns: A outbox CQRS, B CQRS with event history, C orchestrated process, D simple aggregates.

Options considered:

1. Aggregates only where we own identity and lifecycle; the engine stays value objects and pure domain services.
1. Turn the upstream facts (zones, rules, address points) into entities and aggregates too.
1. Event history for the server aggregates (pattern B).

## Decision

We model aggregates where we own identity and lifecycle, and keep the engine as it is (option 1).

- **Parking data context (server, pattern A):** `CoverageArea` (with child `PublishedDataset` entities and the
  publication guard), `RefreshRun` (with child `SourceFetch` entities and a status machine), `DatasetSnapshot` and
  `UserAccount` (with child `DeviceSession` entities, rotation and reuse revocation).
- **Driver assistant context (phone, pattern D):** `ResidentCard`, `SavedPlaces` (Home, Work, Favourite places) and
  `ParkingSession` (parked car and timer).
- Strongly typed IDs (`StronglyTypedGuidId<T>`) per aggregate and entity; value objects for `AreaCode`,
  `PublicationGuard`, `ContentHash`, `FreshnessPolicy`, `FailureCode`, `SubjectHash`, `RefreshTokenHash`,
  `CardNumber`, `LicencePlate` and the others listed in `docs/architecture-v2.md` section 5.1; enumerations instead of
  free strings.
- Domain events on the server aggregates (`DatasetPublishedDomainEvent`, `RefreshRunFailedDomainEvent`,
  `CoverageAreaScheduleChangedDomainEvent`, `RefreshRequestedDomainEvent` and others) use primitive payloads and the
  Newtonsoft constructor that keeps their `Id`. No integration events.
- The upstream facts stay value objects; the snapshot rows are immutable facts keyed by a snapshot ID, not aggregate
  state.
- The engine's value objects and services are not renamed, moved or reshaped. Bridges (`ResidentCard.ToParkingCard()`,
  `ParkingSession.ToParkedCar()`, `CoverageArea.ToMunicipality()`) produce the records the engine already takes, each
  with a characterisation test. Refactoring commits stay separate from behaviour commits, and the parity suite stays
  green at every commit.

## Consequences

### Positive

- Every new business rule (guard, freshness, sessions, card scope, one Home and one Work, timer bounds) has a home.
- The parity proof is untouched: the engine does not change.
- The run history is the state of `RefreshRun`, so no event history is needed for audit.

### Negative

- Two styles live side by side in one Domain project: aggregates and a functional engine.
- Bridges must be kept in step with the records they produce.
- The phone's aggregates raise no events, so a future sync needs its own design.

## Alternatives considered

- **Upstream facts as aggregates** - rejected. They have no identity or lifecycle in this system; making them entities
  adds persistence machinery and risks parity for no modelling gain.
- **Event history (pattern B)** - rejected. The run history already records every attempt; event history would add
  upcasting work and storage for nothing new.
