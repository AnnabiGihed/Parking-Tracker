# ADR-0013: Run refresh jobs with Hangfire on PostgreSQL storage, outside Pivot's scheduling package

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The service needs recurring jobs: one daily refresh per municipality (19), a daily region job, an hourly roadworks
job, a City document check every 6 hours and a daily cleanup. They must run in Brussels time, staggered, with retries,
one at a time per area, and with a dashboard for admins. The owner decided PostgreSQL (ADR-0010) and Google sign-in
without Keycloak (ADR-0014).

Pivot's house scheduler is Hangfire through `Pivot.Framework.Infrastructure.Scheduling`. Evidence from the
Pivot.Framework source at commit `c6c6bf0` (2026-11-10), file
`Src/Infrastructure/Pivot.Framework.Infrastructure.Scheduling/Extensions/HangfireExtensions.cs` and the recurring-job
service next to it:

- `AddHangfireWithDashboard` hard-codes `UseSqlServerStorage` and offers no storage callback.
- `IRecurringJobService.CreateJob` hands Hangfire a captured `Func<>`, which Hangfire cannot reliably serialise
  (listed among the framework's known defects).
- `RecurrenceConfig.Daily` always means 00:00 UTC: no staggering, no `Europe/Brussels` time zone.
- The dashboard authorization filter expects a Keycloak token and checks authentication but no role.
- The only cached package version is 1.1.7, while the project file says 1.0.0.

The package therefore cannot run on PostgreSQL or with Google sign-in today.

Options considered:

1. Hangfire 1.8 with `Hangfire.PostgreSql`, configured directly in the Jobs host.
1. Pivot's `AddHangfireWithDashboard` and `IRecurringJobService`.
1. Extend Pivot's scheduling package first and publish a new Pivot version.
1. Quartz.NET.
1. A `BackgroundService` with a cron parser and an own run table.

## Decision

We use Hangfire 1.8 with `Hangfire.PostgreSql` directly, outside `Pivot.Framework.Infrastructure.Scheduling`. This is
a recorded deviation from the Pivot frame.

- Storage: schema `hangfire` in the parking database, role `pt_jobs`.
- One Hangfire server, in the Jobs host only, with two workers.
- Recurring jobs are registered with `IRecurringJobManager.AddOrUpdate<TJob>` and method-call expressions taking only
  primitive arguments (the area code), with `RecurringJobOptions.TimeZone` set to `Europe/Brussels`.
- `DisableConcurrentExecution` per area; `AutomaticRetry` with 3 attempts after 5, 15 and 60 minutes, for transient
  failures only.
- The dashboard is mounted in the admin app against the same storage, with no server, behind the admin policy
  (ADR-0017).
- The domain run history is the `RefreshRun` aggregate, not Hangfire's job history.
- Package versions are pinned in `Directory.Packages.props` after checking `Hangfire.PostgreSql` against Hangfire 1.8
  on .NET 10 in stage S4.

Upstream improvement for Pivot.Framework (an issue opened in stage S0): a storage callback on
`AddHangfireWithDashboard`, expression-based recurring jobs with a time zone, and a dashboard filter that takes an
authorization policy. When a Pivot version ships them, a new ADR can move the Jobs host back to the Pivot package and
supersede this one.

## Consequences

### Positive

- The house scheduler (Hangfire) with the storage the owner chose.
- Persistent queues, retries, per-area locking and a dashboard without writing them.
- Brussels-time schedules, staggered between 02:30 and 05:00.

### Negative

- A community storage package with its own release cycle.
- A second scheduling library (Quartz) stays in the dependency graph through Pivot's messaging package.
- The deviation must be tracked until Pivot catches up.

## Alternatives considered

- **Pivot's scheduling package as it is** - rejected. It cannot use PostgreSQL, its recurring jobs are not reliably
  serialisable, and its schedules ignore the time zone.
- **Extend Pivot first** - rejected for now. It puts a framework release on the critical path of this work; the fix
  is proposed upstream instead and can replace this deviation later.
- **Quartz.NET** - rejected. No dashboard, no per-job retry policy, and not the house scheduler.
- **`BackgroundService` with a cron parser** - rejected. It re-implements a scheduler, retries and locking.
