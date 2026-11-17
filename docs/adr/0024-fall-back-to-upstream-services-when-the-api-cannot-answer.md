# ADR-0024: Fall back to the upstream services when the API cannot answer, switchable remotely

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The app's honesty rule is "never FREE without confirmed data; any missing source gives CAN'T VERIFY". With a data
service (ADR-0009), the network boundary adds new failure modes: the VPS can be down, a dataset can be stale, a token
can be rejected, a rate limit can be hit, or the server clock can be wrong. One VPS without an SLA (ADR-0022) must not
make the app less available than today, when it reads the upstream services directly. The upstream licences may not
allow redistributing their data through our API (open question Q1), and the owner must be able to keep public users on
upstream data until that is answered. Sign-in is required (ADR-0015), but the upstream services need no account.

Options considered:

1. A data source mode switchable remotely, with a fallback to the upstream services.
1. API only.
1. Upstream only (today's behaviour).

## Decision

We add a data source mode with an upstream fallback, switchable from the server (option 1).

- **Honesty across the boundary.** Every non-success outcome of an API call is a failed source for the decider, never
  FREE and never an earlier answer: a timeout (12 s), a connection or TLS failure, a `401` that a token refresh does
  not fix, `429`, `5xx`, `503` with `parking-data-stale`, an unparseable body, or a `verifiedAt` older than the maximum
  age by the phone's own clock. The adapter adds the failure codes `ParkingApi.Unavailable` and `ParkingApi.Stale`.
- **Modes.** `Upstream` (today), `ApiWithUpstreamFallback` (API first; on a failure above, the existing upstream
  client is asked; if that fails too, CAN'T VERIFY) and `Api` (no fallback).
- **Switch.** The mode comes from `GET /api/v1/client-config`, cached on the phone, with a built-in default per
  release. A phone that cannot reach the API uses its cached or built-in mode.
- **Rollout.** The first release with sign-in ships with `Upstream`; the owner's account gets
  `ApiWithUpstreamFallback`; everyone gets it only after Q1 is answered yes. Retiring the fallback needs a new ADR.
- Network errors never sign the user out (ADR-0015).
- No offline answers from cached data (decided by default; a new ADR would be needed).

## Consequences

### Positive

- A VPS outage costs no more availability than today when the fallback is on.
- The owner can move every phone back to upstream data without an app release.
- Public release of API-served data can wait for the licence answer without blocking the rest of the work.

### Negative

- Two data paths stay in the app, and both must be tested (the emulator test covers API down, `429`, `5xx` and stale).
- With the fallback, bounding boxes still reach the upstream services, as today.
- The fallback hides some API problems from users, so monitoring must catch them (`/health/data`, admin alerts).
- A stale-but-wrong client config cached on a phone keeps its mode until the next successful fetch.

## Alternatives considered

- **API only** - rejected. Every VPS outage would be a CAN'T VERIFY everywhere, and public users could not be kept on
  upstream data while the licence question is open.
- **Upstream only** - rejected. It does not meet the owner's requirement that the API is the main data provider.
