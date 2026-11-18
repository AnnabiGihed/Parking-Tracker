# ADR-0010: Use PostgreSQL with PostGIS through Pivot's PostgreSQL persistence package

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

The parking data service (ADR-0009) stores about 25 k street surfaces, 170 k address points and 150 k on-street spaces
per generation, plus coverage areas, refresh runs, accounts and the outbox. The owner decided PostgreSQL, with PostGIS
allowed. Pivot.Framework's default persistence is SQL Server; `Pivot.Framework.Infrastructure.Persistence.PostgreSQL`
provides `AddPostgreSqlContext<TContext>` and PostgreSQL configurations for the outbox on top of
`Pivot.Framework.Infrastructure.Persistence.EntityFrameworkCore`.

The parity fixtures were recorded against WFS `bbox` queries, which select features whose geometry intersects the
box. The server must return the same features, or the free-spot search may pick a candidate the recorded run never
saw.

Options considered:

1. PostgreSQL with PostGIS geometry columns and GiST indexes.
1. PostgreSQL without PostGIS: GeoJSON text plus four bounding-box columns.
1. SQL Server, the Pivot default.

## Decision

We use PostgreSQL 17 with the PostGIS extension through `Pivot.Framework.Infrastructure.Persistence.PostgreSQL`.

- `ParkingDataDbContext` derives from `PivotDbContextBase`; aggregates go through `UnitOfWork<TContext>` and
  `BaseAsyncCommandRepository`.
- `AddPostgreSqlContext<ParkingDataDbContext>` is called with an options callback that adds `UseNetTopologySuite()`
  to Npgsql (to verify against the 2.1.0 package in stage S4).
- Geometries are stored in EPSG:3857 exactly as upstream sends them, without reprojection, with GiST indexes; box
  queries use `ST_Intersects`.
- Snapshot rows are bulk-loaded with Npgsql binary `COPY` into rows keyed by the snapshot ID. This bypasses the unit of
  work and is a recorded deviation: the rows are immutable facts, not aggregate state, and the snapshot's publication
  goes through the unit of work (ADR-0011).
- One database with one role per host (`pt_owner`, `pt_api`, `pt_jobs`, `pt_admin_hangfire`); Hangfire uses the schema
  `hangfire` (ADR-0013).
- NetTopologySuite types stay in `ParkingTracker.Persistence.EntityFrameworkCore`, never in Domain.

## Consequences

### Positive

- Exact intersection semantics, so API-mode parity can hold on the recorded fixtures.
- Fast box queries on hundreds of thousands of rows with GiST.
- One database server for the API, the Jobs host and Hangfire.
- Untouched EPSG:3857 coordinates keep the floating-point values the decider sees identical to today's.

### Negative

- An extension to install and upgrade, and a PostGIS image (`postgis/postgis`) instead of the plain PostgreSQL image.
- The bulk `COPY` path is a second way of writing that the unit of work does not cover; it needs its own tests.
- Pivot's EF Core persistence package still references the SQL Server provider, which lands in the server images
  unused.

## Alternatives considered

- **PostgreSQL without PostGIS** - rejected. Envelope overlap returns a superset of what WFS returned and breaks
  API-mode parity in edge cases near the box border.
- **SQL Server** - rejected. It contradicts the owner's decision and needs about 2 GB of RAM on the VPS.
