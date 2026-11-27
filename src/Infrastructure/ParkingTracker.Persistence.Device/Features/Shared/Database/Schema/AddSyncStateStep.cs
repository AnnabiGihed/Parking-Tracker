namespace ParkingTracker.Persistence.Device.Features.Shared.Database.Schema;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Step 3 of the schema: the <c>sync_state</c> table of the profile sync (ADR-0032). It holds one row at most, as the
///              <c>id</c> column is always 1: the revision of the server copy the phone last read (<c>base_revision</c>, empty before the
///              first read), whether the phone changed since then (<c>dirty</c>), and the moment each synced section last changed
///              (home, work, favourites, vehicle and card), in milliseconds since 1970 like the other moment columns. The
///              <c>erase_pending</c> flag is set once the server confirmed the deletion of the account and cleared with the row by the full wipe,
///              so that a wipe that did not finish is run again after a restart. Only the account flow and the wipes write the
///              table before the sync service exists. Once a release ships this step, never edit it: a change is a new step.
/// </summary>
internal sealed class AddSyncStateStep : IDeviceSchemaStep
{
	#region Properties
	/// <inheritdoc />
	public int Version => 3;

	/// <inheritdoc />
	public IReadOnlyList<string> Statements { get; } =
	[
		"""
		CREATE TABLE sync_state (
			id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
			base_revision INTEGER NULL,
			dirty INTEGER NOT NULL DEFAULT 0,
			home_updated_at INTEGER NULL,
			work_updated_at INTEGER NULL,
			favourites_updated_at INTEGER NULL,
			vehicle_updated_at INTEGER NULL,
			card_updated_at INTEGER NULL,
			erase_pending INTEGER NOT NULL DEFAULT 0)
		""",
	];
	#endregion
}
