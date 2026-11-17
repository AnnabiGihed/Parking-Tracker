using Android.Gms.Location;
using AndroidLocation = Android.Locations.Location;

namespace ParkingTracker.App.Features.SpotDecisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The Java-callback glue of <see cref="FusedLocationTracker"/>: a <see cref="LocationCallback"/> that hands each position
///              and each change of availability to delegates, so that the tracker holds all the logic and the Java object stays small.
///              The platform calls it on the looper it was registered with.
/// </summary>
internal sealed class FusedLocationCallback : LocationCallback
{
	#region Fields
	/// <summary>
	/// Receives the last position of each result.
	/// </summary>
	private readonly Action<AndroidLocation> onLocation;

	/// <summary>
	/// Receives whether the device can deliver positions right now.
	/// </summary>
	private readonly Action<bool> onAvailability;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FusedLocationCallback"/> class.
	/// </summary>
	/// <param name="onLocation">Receives the last position of each result.</param>
	/// <param name="onAvailability">Receives whether the device can deliver positions right now.</param>
	public FusedLocationCallback(Action<AndroidLocation> onLocation, Action<bool> onAvailability)
	{
		ArgumentNullException.ThrowIfNull(onLocation);
		ArgumentNullException.ThrowIfNull(onAvailability);
		this.onLocation = onLocation;
		this.onAvailability = onAvailability;
	}
	#endregion

	#region LocationCallback Overrides
	/// <inheritdoc />
	public override void OnLocationResult(LocationResult result)
	{
		if (result.LastLocation is { } location)
		{
			onLocation(location);
		}
	}

	/// <inheritdoc />
	public override void OnLocationAvailability(LocationAvailability locationAvailability)
	{
		onAvailability(locationAvailability.IsLocationAvailable);
	}
	#endregion
}
