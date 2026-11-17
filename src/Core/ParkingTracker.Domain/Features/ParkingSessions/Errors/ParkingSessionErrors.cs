using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.ParkingSessions.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the parking session; each code is stable and dotted.
/// </summary>
public static class ParkingSessionErrors
{
	#region Static Instances
	/// <summary>
	/// The position is not a real place on Earth.
	/// </summary>
	public static readonly Error PositionInvalid = new("ParkingSession.Position.Invalid", "The position needs a latitude between -90 and 90 and a longitude between -180 and 180.");

	/// <summary>
	/// The session was closed already and cannot change.
	/// </summary>
	public static readonly Error AlreadyEnded = new("ParkingSession.AlreadyEnded", "The parking session has ended already.");

	/// <summary>
	/// The timer is shorter than 5 minutes or longer than one day.
	/// </summary>
	public static readonly Error TimerLengthOutOfRange = new("ParkingSession.Timer.LengthOutOfRange", "A timer lasts between 5 minutes and one day (1440 minutes).");

	/// <summary>
	/// The timer ends at or before the moment the car was parked.
	/// </summary>
	public static readonly Error TimerEndsBeforeParking = new("ParkingSession.Timer.EndsBeforeParking", "The timer has to end after the moment the car was parked.");

	/// <summary>
	/// The session ends before it started.
	/// </summary>
	public static readonly Error EndedBeforeParked = new("ParkingSession.EndedBeforeParked", "The parking session cannot end before the car was parked.");
	#endregion
}
