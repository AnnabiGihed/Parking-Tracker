# ADR-0018: Keep the driver's data in an encrypted on-device SQLite database through Pivot's SQLite package

- Status: Superseded by ADR-0026
- Date: 2026-11-17
- Deciders: Gihed Annabi
- Supersedes: ADR-0008

## Context

ADR-0004, refined by ADR-0008, migrated the saved data of the Capacitor app once, kept the card in `SecureStorage` and
every other key in `Preferences`, and read the old values from the LevelDB files first with a hidden web view as the
fallback. ADR-0001 rejected `Pivot.Framework.Infrastructure.Persistence.Sqlite` because five settings values did not
need EF Core.

What changed: the owner requires a local database on the phone for the card, home, work and similar data, and a domain
model with aggregates (ADR-0019). Places and parking sessions are relational and have invariants; key-value storage no
longer fits. The storage part of ADR-0008 ("`pt.card` goes to `SecureStorage`, every other key to `Preferences`")
changes; its reading rules do not, and are restated below so that this ADR can replace it.

Forces: the APK size and cold start already grew with .NET (ADR-0001); the phone's data is personal (ADR-0015); a
restore on a new phone cannot read the Android Keystore.

Options considered:

1. EF Core with `Pivot.Framework.Infrastructure.Persistence.Sqlite` and field-level encryption.
1. sqlite-net-pcl with hand-written repositories.
1. Keep `SecureStorage` and `Preferences`.
1. SQLCipher for the whole file.

## Decision

We keep the driver's data in a SQLite database through EF Core and Pivot's SQLite package (option 1), behind a budget
gate.

- `DeviceDbContext` on `PivotDbContextBase`, registered with `AddSqliteContext<DeviceDbContext>`; repositories on
  `BaseAsyncCommandRepository`; a `DeviceUnitOfWork`; migrations in `ParkingTracker.Persistence.Device`, applied by
  `DeviceDatabaseInitializer` in the start-up gate; a compiled model.
- `AddEfCoreWritePersistence` lives in Pivot's messaging package (RabbitMQ.Client, Quartz), so the device registers
  the persistence types through its own installer and replaces the HTTP-context current-user provider with a device
  provider. This is a recorded deviation; the upstream fix is a persistence-only registration in the persistence
  package.
- Tables: `resident_cards`, `saved_places`, `parking_sessions`, `device_owner` and the framework's outbox table, which
  stays empty (no events are dispatched on the phone).
- `FieldCipher` (AES-GCM, random 96-bit nonce, key ID prefix) encrypts the card number, the plate and the coordinates
  of saved places and of the parked car. The 256-bit key is created on first use and stored in `SecureStorage` under
  `pt.dbKey`. The database file and the key are excluded from Android backup. A missing key leaves those fields empty
  and the card dialog asks again.
- `SettingsToDatabaseMigration` runs once after the legacy migration: it moves `pt.card`, `pt.car` and `pt.timer`
  into the aggregates in one unit of work, never overwrites a row, keeps the old keys in that release, writes the
  marker `pt.dbMigration` and gives up after three failed attempts. Settings without invariants stay in
  `Preferences`.
- **Budget gate.** Stage S3 opens with a spike. If EF Core adds more than 8 MB to the Release APK or more than 300 ms
  to cold start on the API 34 emulator, we fall back to sqlite-net-pcl with a new ADR.

Rules of ADR-0008 (and ADR-0004) that stay in force:

- `LegacyLocalStorageReader` reads the LevelDB `*.log` and `*.ldb` files first; the hidden web view
  (`WebViewLegacyStorageReader`) is asked only when the directory exists but the parse fails; a successful answer of
  either reader is final, an empty one included.
- The six `pt.*` keys keep their names and JSON shapes; a value that already exists is never overwritten; the old
  storage is never changed or deleted; when the card cannot be brought over, the welcome dialog asks for it.
- Failed attempts are counted in `pt.migrationAttempts`; the third failure writes the marker `pt.migration` with the
  outcome `gaveUp`; the whole run is limited to 15 seconds; the marker records the reader that ran.

## Consequences

### Positive

- The same aggregate, repository and unit-of-work contracts on the phone as on the server, with migrations.
- Sensitive fields are encrypted without a native crypto library.
- Relational structure for places and parking sessions.

### Negative

- Pivot's EF Core persistence package brings the SQL Server provider and ASP.NET Core HTTP abstractions into the APK.
- EF Core model building adds start-up time; the budget gate may force the fallback.
- A restore on a new phone cannot decrypt the fields; the driver enters the card again.
- A second migration step in the start-up gate, with its own upgrade test.
- A device-side registration deviation until Pivot moves the persistence registration.

## Alternatives considered

- **sqlite-net-pcl** - rejected as the first choice. Hand-written repositories and unit of work where Pivot provides
  them; it stays the fallback of the budget gate.
- **Keep `SecureStorage` and `Preferences`** - rejected. No relational structure, does not meet the owner's
  requirement.
- **SQLCipher** - rejected. The official builds need a licence and the free ones are community builds; one more native
  library per ABI.
