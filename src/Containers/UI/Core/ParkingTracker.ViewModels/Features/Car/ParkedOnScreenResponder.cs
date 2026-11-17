using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.ViewModels.Features.Car;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the screen does when the background watcher finds a parking while the app is on screen: it shows the message
///              "Parked: headline" instead of a notification, reads the saved car again (the watcher saved it) and checks the
///              place, as the first version did. The host relay only passes the event here, so the behaviour is testable
///              without a phone.
/// </summary>
public sealed class ParkedOnScreenResponder
{
	#region Fields
	/// <summary>
	/// The messages of the screen.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// The car dialog, whose saved car is read again.
	/// </summary>
	private readonly CarViewModel car;

	/// <summary>
	/// The spot check, which checks the place where the car stands.
	/// </summary>
	private readonly SpotCheckViewModel spot;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkedOnScreenResponder"/> class.
	/// </summary>
	/// <param name="toast">The messages of the screen.</param>
	/// <param name="car">The car dialog, whose saved car is read again.</param>
	/// <param name="spot">The spot check, which checks the place where the car stands.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public ParkedOnScreenResponder(ToastViewModel toast, CarViewModel car, SpotCheckViewModel spot)
	{
		ArgumentNullException.ThrowIfNull(toast);
		ArgumentNullException.ThrowIfNull(car);
		ArgumentNullException.ThrowIfNull(spot);
		this.toast = toast;
		this.car = car;
		this.spot = spot;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Shows the message, reads the saved car again and checks the place where the car stands; the check is skipped while the app
	/// follows the user's position.
	/// </summary>
	/// <param name="parked">The headline, the place and the accuracy of the parking that was found.</param>
	/// <returns>A task that completes when the check ended.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="parked"/> is <see langword="null"/>.</exception>
	public async Task RespondAsync(ParkedOnScreenEventArgs parked)
	{
		ArgumentNullException.ThrowIfNull(parked);
		toast.Show($"🚗 Parked: {parked.Headline}");
		await car.LoadAsync().ConfigureAwait(false);
		await spot.CheckParkedPlaceAsync(parked.Point, parked.AccuracyMetres).ConfigureAwait(false);
	}
	#endregion
}
