namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Which answer a read of the profile got.
/// </summary>
public enum ProfileGetOutcome
{
	/// <summary>
	/// 304: the revision the phone sent is still current.
	/// </summary>
	NotModified = 0,

	/// <summary>
	/// 200: the server sent a document and its revision.
	/// </summary>
	Found = 1,

	/// <summary>
	/// 404: the server has no document for the account.
	/// </summary>
	NotFound = 2,
}
