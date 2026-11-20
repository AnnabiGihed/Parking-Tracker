namespace ParkingTracker.Persistence.Device.Features.Shared.Database.Schema;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Step 2 of the schema: the <c>vehicles</c> table of the one vehicle profile (ADR-0031). The make is the code of the
///              curated list; the fuel, the emission standard and the plugs are the numbers of their enums, the plugs as a JSON array;
///              the battery is in kWh and the range in kilometres; the plate holds the value encrypted by the field cipher. As in
///              <c>resident_cards</c>, a <c>slot</c> column that is always 1 and unique lets the database hold one profile at most.
///              Once a release ships this step, never edit it: a change is a new step.
/// </summary>
internal sealed class AddVehiclesStep : IDeviceSchemaStep
{
	#region Properties
	/// <inheritdoc />
	public int Version => 2;

	/// <inheritdoc />
	public IReadOnlyList<string> Statements { get; } =
	[
		"""
		CREATE TABLE vehicles (
			id TEXT NOT NULL PRIMARY KEY,
			make TEXT NOT NULL,
			model TEXT NOT NULL DEFAULT '',
			model_year INTEGER NOT NULL,
			fuel INTEGER NOT NULL,
			euro_norm INTEGER NOT NULL DEFAULT 0,
			connectors TEXT NOT NULL DEFAULT '[]',
			battery_kwh REAL NULL,
			range_km INTEGER NULL,
			plate TEXT NULL,
			slot INTEGER NOT NULL DEFAULT 1 UNIQUE CHECK (slot = 1))
		""",
	];
	#endregion
}
