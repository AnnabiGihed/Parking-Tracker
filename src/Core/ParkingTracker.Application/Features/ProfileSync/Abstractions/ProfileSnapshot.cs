using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The profile of the phone at one moment, read in one go (ADR-0032): the local data as a profile document, and the sync
///              state read at the same time. A section that is present carries its own moment in the document (the Unix epoch when it
///              never changed here, which the merge reads as "no moment"); a section the user cleared is absent from the document and
///              keeps its moment in <see cref="SyncState.Times"/>.
/// </summary>
/// <param name="Document">The local data as a profile document.</param>
/// <param name="State">The sync state read with it.</param>
public sealed record ProfileSnapshot(ProfileDocument Document, SyncState State)
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the phone holds no section at all.
	/// </summary>
	public bool IsEmpty => Document.Home is null && Document.Work is null && Document.Vehicle is null && Document.Card is null && Document.Favourites.Places.Count == 0;
	#endregion
}
