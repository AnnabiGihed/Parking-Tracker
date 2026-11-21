namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The record that an account was erased, kept by the subject hash so that an erasure is applied again after a
///              backup restore. It is a row of the persistence layer, not a domain concept, and it is purged after six months.
/// </summary>
public sealed class ErasureLogEntry
{
	#region Properties
	/// <summary>
	/// Gets the keyed hash of the erased subject, as 64 lowercase hexadecimal characters.
	/// </summary>
	public required string SubjectHash { get; init; }

	/// <summary>
	/// Gets or sets the day the account was erased; it moves to the latest day when the same subject is erased again.
	/// </summary>
	public required DateOnly ErasedOn { get; set; }
	#endregion
}
