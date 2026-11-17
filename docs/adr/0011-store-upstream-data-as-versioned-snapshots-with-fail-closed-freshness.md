# ADR-0011: Store upstream data as versioned snapshots per area with a publication guard and fail-closed freshness

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

Today the app reads every source live and answers CAN'T VERIFY when a source fails. With a data service (ADR-0009),
the data the phone uses can be hours old, a refresh can fail half-way, and an upstream service can return an empty or
truncated answer. An empty zone layer would read as "unregulated, free for everyone": exactly the wrong FREE the
product exists to avoid.

Options considered:

1. Immutable snapshots per coverage area and source, published only after validation and a row-count guard, never
   served past a maximum age.
1. Overwrite tables in place on each run and serve whatever is there with an `asOf` field.
1. Serve stale data with a warning flag and let the app decide.

## Decision

We store every dataset as an immutable snapshot per coverage area (19 municipalities and the region) and source, and
we fail closed on freshness.

- A run stages rows into a new snapshot. The `CoverageArea` aggregate publishes it only if it passes the shape checks
  and the `PublicationGuard`: zero rows, or a drop of more than 20 % against the published count, is rejected (per
  source configurable). A rejected snapshot waits for an admin to approve or discard it.
- An unchanged content hash publishes nothing and only moves `VerifiedAtUtc`: freshness means verified, not changed.
- Maximum ages (decided by default, the owner may change them): 36 h for daily sources, 3 h for roadworks (refreshed
  hourly), 7 h for the City documents (checked every 6 hours), 7 days for car parks and charging points, which are
  never used for a verdict.
- A dataset older than its maximum age is never served for a decision: the API answers `503` with the problem type
  `parking-data-stale`, which the phone maps to a failed source and the decider to CAN'T VERIFY.
- A box that touches any area whose dataset is not fresh gets `503`, so a stale neighbouring municipality near a
  border gives CAN'T VERIFY (decided by default).
- The phone also checks `verifiedAt` against its own clock.
- Superseded snapshots are deleted after 7 days, rejected ones after 30.

## Consequences

### Positive

- A failed, half-finished or suspicious run never changes what is served.
- Neither a server bug nor an old client can turn stale data into FREE: both ends check.
- Rollback is a pointer switch to the previous snapshot.

### Negative

- Storage for several generations, and a cleanup job to run.
- When a municipality's job keeps failing beyond its maximum age, that municipality answers CAN'T VERIFY (or falls back
  to upstream, ADR-0024) until a run succeeds.
- A legitimate large drop upstream (a zone removed) waits for an admin's approval.
- Answers near municipality borders depend on two areas being fresh.

## Alternatives considered

- **Overwrite in place** - rejected. A run that fails half-way leaves mixed data, and an empty upstream answer erases
  the zones and produces false FREE answers.
- **Serve stale data with a warning** - rejected. The honesty rule would then depend on every client version being
  correct.
