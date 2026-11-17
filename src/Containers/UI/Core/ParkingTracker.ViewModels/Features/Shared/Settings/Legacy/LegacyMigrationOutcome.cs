namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What one run of <see cref="LegacyDataMigration"/> did, so that the host can log it and the tests can assert it.
/// </summary>
public enum LegacyMigrationOutcome
{
	/// <summary>
	/// The marker <c>pt.migration</c> exists: a run of an earlier launch finished, nothing was read.
	/// </summary>
	AlreadyDone,

	/// <summary>
	/// At least one value was copied from the old storage and the marker was written.
	/// </summary>
	Migrated,

	/// <summary>
	/// The old storage was read but held no usable value (a new install, or only unreadable values); the marker was written.
	/// </summary>
	NothingFound,

	/// <summary>
	/// The old storage could not be read or a value could not be saved; no marker was written, so the next launch tries again.
	/// </summary>
	WillRetry,

	/// <summary>
	/// The old storage could not be read for the last allowed time; the marker records the failure and the user enters the card again.
	/// </summary>
	GaveUp,
}
