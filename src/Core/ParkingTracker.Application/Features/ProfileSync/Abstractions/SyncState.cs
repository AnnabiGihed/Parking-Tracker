namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the phone remembers about its sync with the account (ADR-0032), from the <c>sync_state</c> row: the revision of the
///              server copy it last agreed with, whether it changed since, and when each section changed. No row reads as
///              <see cref="Empty"/>.
/// </summary>
/// <param name="BaseRevision">The revision of the server copy the phone last read or wrote, or <see langword="null"/> before the first sync.</param>
/// <param name="IsDirty">Whether the phone changed a section since the base revision.</param>
/// <param name="Times">When each section last changed on this phone.</param>
public sealed record SyncState(long? BaseRevision, bool IsDirty, SectionTimes Times)
{
	#region Static Instances
	/// <summary>
	/// The state of a phone that never synced and changed nothing.
	/// </summary>
	public static readonly SyncState Empty = new(null, false, SectionTimes.None);
	#endregion
}
