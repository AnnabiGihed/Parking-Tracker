namespace ParkingTracker.Application.Features.SpotDecisions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Event data of a failure to read the position from <see cref="ILocationTracker"/>, for example a refused permission.
/// </summary>
public sealed class LocationErrorEventArgs : EventArgs
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LocationErrorEventArgs"/> class.
	/// </summary>
	/// <param name="message">What went wrong, in words that can be shown to the user.</param>
	/// <param name="kind">Whether the tracker stopped or only has no position for now.</param>
	public LocationErrorEventArgs(string message, LocationErrorKind kind)
	{
		Message = message;
		Kind = kind;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets what went wrong, in words that can be shown to the user, for example <c>location not allowed</c>.
	/// </summary>
	public string Message { get; }

	/// <summary>
	/// Gets whether the tracker stopped or only has no position for now.
	/// </summary>
	public LocationErrorKind Kind { get; }
	#endregion
}
