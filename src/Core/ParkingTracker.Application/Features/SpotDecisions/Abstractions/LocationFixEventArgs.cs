using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;

namespace ParkingTracker.Application.Features.SpotDecisions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Event data of a new position fix from <see cref="ILocationTracker"/>.
/// </summary>
public sealed class LocationFixEventArgs : EventArgs
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LocationFixEventArgs"/> class.
	/// </summary>
	/// <param name="fix">The fix the device reported.</param>
	public LocationFixEventArgs(GpsFix fix)
	{
		Fix = fix;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the fix the device reported: position, accuracy, speed when known and the time of the reading.
	/// </summary>
	public GpsFix Fix { get; }
	#endregion
}
