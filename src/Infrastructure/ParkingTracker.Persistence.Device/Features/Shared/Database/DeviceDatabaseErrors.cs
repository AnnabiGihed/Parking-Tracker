using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the device database: the missing key, the encrypted fields, the schema and the transaction. Each code is
///              stable and dotted; the messages are for the log, because the driver never sees them.
/// </summary>
public static class DeviceDatabaseErrors
{
	#region Static Instances
	/// <summary>
	/// This phone has no key with the identifier that an encrypted value names, as after a restore on a new phone; the value can
	/// never be read. A key that only cannot be read now is <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/>.
	/// </summary>
	public static readonly Error KeyMissing = new("DeviceDatabase.Key.Missing", "This phone has no key for the encrypted value.");

	/// <summary>
	/// An encrypted value is not in the format the cipher writes.
	/// </summary>
	public static readonly Error CiphertextMalformed = new("DeviceDatabase.Cipher.Malformed", "The encrypted value is not in a known format.");

	/// <summary>
	/// An encrypted value does not authenticate: it was changed, or it was written with another key.
	/// </summary>
	public static readonly Error CiphertextRejected = new("DeviceDatabase.Cipher.Rejected", "The encrypted value was changed or belongs to another key.");

	/// <summary>
	/// The database was written by a newer version of the app, so this version must not change it.
	/// </summary>
	public static readonly Error SchemaNewerThanApp = new("DeviceDatabase.Schema.NewerThanApp", "The device database was written by a newer version of the app.");

	/// <summary>
	/// The database refused a staged change; every change of the commit was rolled back.
	/// </summary>
	public static readonly Error CommitFailed = new("DeviceDatabase.Commit.Failed", "The device database could not save the changes; none was applied.");

	/// <summary>
	/// The database could not be read.
	/// </summary>
	public static readonly Error ReadFailed = new("DeviceDatabase.Read.Failed", "The device database could not be read.");
	#endregion

	#region Factory Methods
	/// <summary>
	/// Builds the error for a schema step that failed.
	/// </summary>
	/// <param name="version">The version of the step.</param>
	/// <returns>The error.</returns>
	public static Error SchemaStepFailed(int version)
	{
		return new Error("DeviceDatabase.Schema.StepFailed", $"Schema step {version} of the device database failed and was rolled back.");
	}

	/// <summary>
	/// Builds the error for a stored row that cannot be turned into an aggregate.
	/// </summary>
	/// <param name="table">The table of the row.</param>
	/// <param name="reason">What is wrong with the row.</param>
	/// <returns>The error.</returns>
	public static Error RowInvalid(string table, string reason)
	{
		return new Error("DeviceDatabase.Row.Invalid", $"A row of '{table}' is not valid: {reason}");
	}
	#endregion
}
