namespace ParkingTracker.Persistence.Device.Features.Shared.Database.Schema;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Step 1 of the schema: the tables of ADR-0026. Fields that the field cipher protects (card number, plate and every
///              coordinate pair) are text columns that hold the encrypted value; lists are JSON text; dates are ISO text and moments
///              are milliseconds since 1970, as the settings of the first version wrote them. Each aggregate keeps its identifier in
///              the rows. The phone holds one card, one list of places and one session, and the database enforces it: a <c>slot</c>
///              column that is always 1 and unique in <c>resident_cards</c> and <c>parking_sessions</c>, and unique indexes on the
///              kind of a home or a work place (kinds 0 and 1) and on the position of a place in <c>saved_places</c>. Once a release
///              ships this step, never edit it: a change is a new step.
/// </summary>
internal sealed class CreateTablesStep : IDeviceSchemaStep
{
	#region Properties
	/// <inheritdoc />
	public int Version => 1;

	/// <inheritdoc />
	public IReadOnlyList<string> Statements { get; } =
	[
		"""
		CREATE TABLE resident_cards (
			id TEXT NOT NULL PRIMARY KEY,
			municipality TEXT NOT NULL,
			sector TEXT NULL,
			valid_from TEXT NULL,
			valid_to TEXT NULL,
			tolerances TEXT NOT NULL,
			mailles TEXT NOT NULL,
			lims TEXT NOT NULL,
			card_number TEXT NULL,
			plate TEXT NULL,
			slot INTEGER NOT NULL DEFAULT 1 UNIQUE CHECK (slot = 1))
		""",
		"""
		CREATE TABLE saved_places (
			id TEXT NOT NULL PRIMARY KEY,
			list_id TEXT NOT NULL,
			position INTEGER NOT NULL,
			kind INTEGER NOT NULL,
			label TEXT NOT NULL,
			address_text TEXT NOT NULL,
			coordinates TEXT NOT NULL)
		""",
		"CREATE UNIQUE INDEX ux_saved_places_position ON saved_places (position)",
		"CREATE UNIQUE INDEX ux_saved_places_one_home_or_work ON saved_places (kind) WHERE kind IN (0, 1)",
		"""
		CREATE TABLE parking_sessions (
			id TEXT NOT NULL PRIMARY KEY,
			coordinates TEXT NOT NULL,
			parked_at INTEGER NOT NULL,
			headline TEXT NOT NULL,
			address_text TEXT NOT NULL,
			timer_end INTEGER NULL,
			timer_minutes INTEGER NULL,
			ended_at INTEGER NULL,
			slot INTEGER NOT NULL DEFAULT 1 UNIQUE CHECK (slot = 1))
		""",
		"""
		CREATE TABLE device_owner (
			id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
			account_hash TEXT NOT NULL,
			bound_at INTEGER NOT NULL)
		""",
	];
	#endregion
}
