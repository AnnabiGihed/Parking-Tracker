# ADR-0012: Drain the outbox in process in the Jobs host only, without a message broker

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

Two reactions must cross from the API to the Jobs host after a commit: an admin changes a schedule (the recurring job
must be registered again) and an admin asks for a run now (a job must be enqueued). Pivot's house frame is RabbitMQ
through the transactional outbox. There is no second service and no external consumer. Pivot's polling outbox drain has
no distributed lock (a known framework limitation), so two processes that drain the same outbox process the same
message twice.

Options considered:

1. An in-process publisher with one drainer, in the Jobs host.
1. RabbitMQ through Pivot's outbox.
1. Both the API and the Jobs host drain the same outbox in process.

## Decision

We register `AddInProcessMessagePublisher()` and `AddOutboxDraining<ParkingDataDbContext>` in background polling mode
with a 5-second interval in the Jobs host only. This is a deviation from the house RabbitMQ frame.

- The API registers the unit of work and writes domain events to the outbox in the same transaction, but never drains.
- Handlers of `CoverageAreaScheduleChangedDomainEvent` and `RefreshRequestedDomainEvent` live in
  `ParkingTracker.Backend.Application` and run in the Jobs host.
- There are no integration events and no `ParkingTracker.IntegrationEvents` project.
- Domain events that cross the outbox declare the Newtonsoft JSON constructor that keeps `Id` and `OccurredOnUtc`, so
  a redelivery is recognisable (known framework defect).
- Retry: `OutboxRetryOptions` with 5 attempts and a failure event.
- At start, the Jobs host re-synchronises every recurring job from the `CoverageArea` schedules, so a lost message
  cannot leave a wrong schedule behind.

## Consequences

### Positive

- No broker to install, patch, secure and back up on the VPS.
- One drainer removes double processing by construction.
- At-least-once delivery after commit for the two reactions that need it.

### Negative

- An admin action takes effect after up to one polling interval.
- If the Jobs host is down, admin actions wait until it starts again.
- A second drainer (for example a second Jobs host) would need a distributed lock first; scaling out is not free.
- A deviation from the house frame that every new contributor must learn from this ADR.

## Alternatives considered

- **RabbitMQ through the outbox** - rejected. A broker to run for two processes on one VPS, with no external consumer
  to justify it.
- **Both hosts drain** - rejected. Without a distributed lock the same message is processed twice.
- **No outbox, call Hangfire from the API** - rejected. Enqueueing is not part of the database transaction of the
  admin command, so a crash between the two can lose the action or run it without the command being stored.
