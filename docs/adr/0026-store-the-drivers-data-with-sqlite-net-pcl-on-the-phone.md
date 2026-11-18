# ADR-0026: Store the driver's data with sqlite-net-pcl on the phone

- Status: Accepted
- Date: 2026-11-18
- Deciders: Gihed Annabi
- Supersedes: ADR-0018

## Context

ADR-0018 keeps the card, the saved places and the parking session in an encrypted SQLite database on the phone, through
EF Core and Pivot's EF Core persistence, behind a budget gate: if EF Core adds more than 8 MB to the Release APK or more
than 300 ms to cold start on the API 34 emulator, we fall back to sqlite-net-pcl with a new ADR. This ADR replaces only
the storage technology of ADR-0018. Every other rule of ADR-0018, and the reading rules of ADR-0008 that it restated,
stays and is restated below so that this ADR can replace it.

What changed: stage S3 ran the budget spike. The `budget-spike.yml` workflow built three Release APKs, installed them on
the API 34 emulator and started each cold six times with `am start -W -S`; the first start of each run is discarded,
the median `TotalTime` of the second round is reported. Each database variant opens a file, creates one table and reads
it at start-up (`Features/Shared/DeviceDatabaseSpike/`).

| Variant | Release APK | Difference | Cold start (median) | Difference | Budget |
| --- | --- | --- | --- | --- | --- |
| Plain app | 68.88 MB | - | 2003 ms | - | - |
| EF Core: Pivot's EF Core persistence and `Microsoft.EntityFrameworkCore.Sqlite` | 108.97 MB | +40.09 MB | 2830 ms | +827 ms | exceeded |
| sqlite-net-pcl 1.11.285 (SQLitePCLRaw 3.0.3 and its own native SQLite) | 71.73 MB | +2.84 MB | 2100 ms | +97 ms | met |

EF Core exceeds both limits by a wide margin: five times the size budget and nearly three times the start-up budget.
Part of the size is not EF Core itself: Pivot 2.1.0 has no SQLite persistence package, and its EF Core persistence
package brings the SQL Server provider (upstream issue AnnabiGihed/Pivot.Framework#8). Even without that, model
building and the provider stay on the cold-start path. sqlite-net-pcl stays well inside both limits.

Forces that stay from ADR-0018: places and parking sessions are relational and have invariants (ADR-0019); the phone's
data is personal (ADR-0015); a restore on a new phone cannot read the Android Keystore; the APK size and cold start
already grew with .NET (ADR-0001) and Radzen (ADR-0020).

Options considered:

1. sqlite-net-pcl with hand-written repositories and row mapping, as the budget gate prescribes.
1. EF Core through Pivot's EF Core persistence, accepting the overrun.
1. EF Core with `Microsoft.EntityFrameworkCore.Sqlite` only, without Pivot's persistence package.
1. Raw `Microsoft.Data.Sqlite` or SQLitePCLRaw with hand-written SQL only.

## Decision

We store the driver's data in a SQLite database through sqlite-net-pcl (option 1). We use no EF Core and no Pivot
persistence package on the phone.

### Repositories and mapping

- `ResidentCard`, `SavedPlaces` and `ParkingSession` each get one repository. The ports are narrow interfaces in the
  phone's Application project (load the aggregate, add, update, remove); the implementations are written by hand over
  `SQLiteAsyncConnection` in `ParkingTracker.Persistence.Device` (ADR-0021), wired by the MAUI host.
- The repositories map each aggregate to private row types (`ResidentCardRow`, `SavedPlaceRow`, `ParkingSessionRow`,
  `DeviceOwnerRow`) and back. Row types are persistence details: they never leave the persistence project, and the
  aggregates carry no sqlite-net attributes. Rehydration goes through the aggregates' own factory methods, so a row
  that breaks an invariant fails with a `Result` error instead of producing an invalid aggregate.
- Still used from Pivot on the phone: the domain building blocks (`Entity`, aggregate roots, `StronglyTypedGuidId`,
  value objects), `Result` and `Error`, the aggregates of ADR-0019, and the Application and dependency-injection
  packages as today.
- Not used on the phone: `IUnitOfWork`, `IAsyncCommandRepository` (its expression-based `ExistsAsync` would need a
  predicate translator from aggregates to rows), `BaseAsyncCommandRepository`, `PivotDbContextBase`, the outbox and
  every `Infrastructure.*` package. The device registration deviation of ADR-0018 disappears with them.

### Unit of work and transactions

- A small device unit of work (own port, `IDeviceUnitOfWork`, `Task<Result> CommitAsync(CancellationToken)`) collects
  the inserts, updates and deletes that the repositories stage during one command.
- `CommitAsync` applies them in one `RunInTransactionAsync` call on the shared connection. sqlite-net commits when the
  delegate returns and rolls back when it throws; the unit of work turns the exception into a failed `Result` and
  clears the staged changes. The cancellation token is checked before the transaction starts, because
  `RunInTransactionAsync` takes none.
- One `SQLiteAsyncConnection` per process, opened with `ReadWrite | Create | FullMutex` in the app's private data
  directory (`parkingtracker.db`); sqlite-net serialises the calls on it.

### Schema versioning

- A `schema_version` table holds one row per applied step (version number and UTC time applied).
- Steps are an ordered list of numbered classes in `ParkingTracker.Persistence.Device`, each with explicit SQL. Step 1
  creates the tables below. A step is never edited once released; a change is a new step.
- `DeviceDatabaseInitializer` runs inside the start-up gate, reads the highest applied version and applies each missing
  step in order, each step and its `schema_version` row in one transaction. A database whose version is higher than
  the app knows stops the initializer with a failed `Result`; nothing is written.
- `CreateTableAsync` and its automatic column additions are not used for the real schema.

### Rules of ADR-0018 that stay in force

- Tables: `resident_cards`, `saved_places`, `parking_sessions` and `device_owner`, one per aggregate plus the owner
  binding; there is no outbox table, because no events are dispatched on the phone.
- `FieldCipher` (AES-GCM, random 96-bit nonce, key ID prefix) encrypts the card number, the plate and the coordinates
  of saved places and of the parked car. The mapper calls it, where ADR-0018 used an EF value converter. The 256-bit
  key is created on first use and stored in `SecureStorage` under `pt.dbKey`. The database file, its journal files and
  the key are excluded from Android backup. A missing key leaves those fields empty and the card dialog asks again.
- `SettingsToDatabaseMigration` runs once after the legacy migration: it moves `pt.card`, `pt.car` and `pt.timer`
  into the aggregates in one unit of work, never overwrites a row, keeps the old keys in that release, writes the
  marker `pt.dbMigration` and gives up after three failed attempts. Settings without invariants stay in
  `Preferences`.
- **Budget gate.** The 8 MB and 300 ms limits on the API 34 emulator stay. The real device database is measured with
  the same method before S3 ends; a release that exceeds them needs a new ADR.

### Rules of ADR-0008 (and ADR-0004) that stay in force

- `LegacyLocalStorageReader` reads the LevelDB `*.log` and `*.ldb` files first; the hidden web view
  (`WebViewLegacyStorageReader`) is asked only when the directory exists but the parse fails; a successful answer of
  either reader is final, an empty one included.
- The six `pt.*` keys keep their names and JSON shapes; a value that already exists is never overwritten; the old
  storage is never changed or deleted; when the card cannot be brought over, the welcome dialog asks for it.
- Failed attempts are counted in `pt.migrationAttempts`; the third failure writes the marker `pt.migration` with the
  outcome `gaveUp`; the whole run is limited to 15 seconds; the marker records the reader that ran.

## Consequences

### Positive

- The database costs 2.84 MB and 97 ms instead of 40.09 MB and 827 ms, inside the budget of ADR-0018.
- No SQL Server provider, ASP.NET Core abstractions or messaging types in the APK, and no registration deviation from
  Pivot on the phone; upstream issues #7 and #8 no longer block the device database.
- The aggregates, their invariants and `Result` stay the same as on the server; only persistence differs.
- The schema is plain SQL in ordered steps, readable and testable on a real SQLite file.

### Negative

- Mapping between aggregates and rows is written by hand for three aggregates and the owner row; each mapper needs
  round-trip tests, including the encrypted fields, the missing-key path and invalid rows.
- No LINQ provider beyond sqlite-net's: simple `Where`, `OrderBy` and `Take` on one row type, no joins, no `Include`;
  anything else is hand-written SQL through `QueryAsync`. Encrypted columns cannot be filtered in SQL.
- The unit of work, the schema step runner and their tests are ours to maintain, where Pivot and EF Core would have
  provided them; the phone and the server use different repository contracts.
- `RunInTransactionAsync` runs a synchronous delegate and takes no cancellation token; a long transaction blocks the
  connection.
- The spike code (`Features/Shared/DeviceDatabaseSpike/`, the `IncludeDeviceDatabase` switch in the app project, the
  spike package versions in `Directory.Packages.props` and `budget-spike.yml`) must be removed or turned into the
  budget check of the real database.
- A restore on a new phone still cannot decrypt the fields; the driver enters the card again.

## Alternatives considered

- **EF Core through Pivot's EF Core persistence** - rejected. Measured at +40.09 MB and +827 ms, far over the gate.
- **EF Core with `Microsoft.EntityFrameworkCore.Sqlite` only** - rejected. Without Pivot's package the SQL Server
  provider leaves, but EF Core, its SQLite provider and model building stay; it would still hand-roll the Pivot
  registration and was not measured inside the budget. The gate names sqlite-net-pcl as the fallback.
- **Raw `Microsoft.Data.Sqlite` or SQLitePCLRaw only** - rejected. Smaller still, but every read, write and parameter
  is hand-written; sqlite-net adds row mapping and transactions for 2.84 MB in total.
- **Keep `SecureStorage` and `Preferences`, or SQLCipher** - rejected for the reasons in ADR-0018: no relational
  structure, and a licensed or community native build per ABI.
