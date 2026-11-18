namespace ParkingTracker.ViewModels.Features.Shared.Settings.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What one run of <see cref="SettingsToDatabaseMigration"/> did, so that the host can log it and the tests can assert it.
/// </summary>
public enum DatabaseMigrationOutcome
{
	/// <summary>
	/// The marker <c>pt.dbMigration</c> exists: a run of an earlier launch finished, nothing was read.
	/// </summary>
	AlreadyDone,

	/// <summary>
	/// At least one value was copied into the database and read back equal; the marker was written.
	/// </summary>
	Migrated,

	/// <summary>
	/// There was nothing to copy: no card or car, only unreadable values, or the database already held them; the marker was written.
	/// </summary>
	NothingFound,

	/// <summary>
	/// The copy failed; no marker was written, so the next launch tries again.
	/// </summary>
	WillRetry,

	/// <summary>
	/// The copy failed for the last allowed time; the marker records it and the user enters the card again.
	/// </summary>
	GaveUp,
}
