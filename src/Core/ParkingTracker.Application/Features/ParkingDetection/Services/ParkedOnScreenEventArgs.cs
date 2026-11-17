using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Application.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Carries a stop that was found while the app was on screen, so that the screen can show a message and check the
///              place: the car was saved, but no notification was sent.
/// </summary>
public sealed class ParkedOnScreenEventArgs : EventArgs
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkedOnScreenEventArgs"/> class.
	/// </summary>
	/// <param name="headline">The friendly headline of the answer saved with the car.</param>
	/// <param name="point">Where the car stands.</param>
	/// <param name="accuracyMetres">The accuracy of the position in metres, or <see langword="null"/> when the device reported none.</param>
	public ParkedOnScreenEventArgs(string headline, GeoPoint point, double? accuracyMetres)
	{
		Headline = headline;
		Point = point;
		AccuracyMetres = accuracyMetres;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the friendly headline of the answer saved with the car, for example <c>Paid parking</c>.
	/// </summary>
	public string Headline { get; }

	/// <summary>
	/// Gets where the car stands.
	/// </summary>
	public GeoPoint Point { get; }

	/// <summary>
	/// Gets the accuracy of the position in metres, or <see langword="null"/> when the device reported none.
	/// </summary>
	public double? AccuracyMetres { get; }
	#endregion
}
