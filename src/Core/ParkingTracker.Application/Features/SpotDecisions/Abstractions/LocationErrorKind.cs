namespace ParkingTracker.Application.Features.SpotDecisions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tells whether a <see cref="LocationErrorEventArgs"/> ended the listening of the <see cref="ILocationTracker"/> or only interrupted it.
/// </summary>
public enum LocationErrorKind
{
	/// <summary>
	/// The tracker stopped: the position is not allowed, location is turned off or the tracker could not start.
	/// </summary>
	Stopped,

	/// <summary>
	/// There is no position for now, but the tracker keeps listening and fixes may follow.
	/// </summary>
	Interrupted,
}
