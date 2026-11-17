using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.ViewModels.Features.Car;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The "My car" dialog and the car chip: saves where the user parked from the position or the pin that is on screen,
///              shows it on the map, opens Google Maps with walking directions back to it, and forgets it. The parking alerts
///              and the timer are a later stage and are not part of this dialog.
/// </summary>
public sealed class CarViewModel : ViewModelBase
{
	#region Fields
	/// <summary>
	/// Port to the saved car.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Opens the walking directions.
	/// </summary>
	private readonly ILinkLauncher launcher;

	/// <summary>
	/// The screen that knows where the user is and what the app said.
	/// </summary>
	private readonly SpotCheckViewModel spot;

	/// <summary>
	/// Shows the messages of the dialog.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// Clock that stamps the saved position.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CarViewModel"/> class; the saved car is read by <see cref="LoadAsync"/>.
	/// </summary>
	/// <param name="settings">Keeps the car.</param>
	/// <param name="launcher">Opens the walking directions.</param>
	/// <param name="spot">The screen that knows where the user is.</param>
	/// <param name="toast">Shows the messages of the dialog.</param>
	/// <param name="clock">Clock that stamps the saved position.</param>
	public CarViewModel(ISettingsStore settings, ILinkLauncher launcher, SpotCheckViewModel spot, ToastViewModel toast, TimeProvider clock)
	{
		this.settings = settings;
		this.launcher = launcher;
		this.spot = spot;
		this.toast = toast;
		this.clock = clock;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the saved car, or <see langword="null"/> when none is saved.
	/// </summary>
	public ParkedCar? Car { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the dialog is open.
	/// </summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets when and where the car was parked, in words; the explanation of the feature when no car is saved.
	/// </summary>
	public string Info
	{
		get
		{
			if (Car is null)
			{
				return "Save where you park, so you can find your car again.";
			}

			var where = string.IsNullOrEmpty(Car.Where) ? string.Empty : " · " + Car.Where;
			return $"Parked {ClockText.DateTime(Car.Time)}{where}";
		}
	}

	/// <summary>
	/// Gets what the app said when the car was parked; empty when unknown.
	/// </summary>
	public string AppSaid => Car?.Status ?? string.Empty;

	/// <summary>
	/// Gets the text of the popup of the car marker on the map, with the HTML escaped; empty when no car is saved.
	/// </summary>
	public string PopupHtml => Car is null ? string.Empty : $"<b>My car</b><br>Parked {System.Net.WebUtility.HtmlEncode(ClockText.DateTime(Car.Time))}";
	#endregion

	#region Events
	/// <summary>
	/// Occurs when the user asked to see the car on the map.
	/// </summary>
	public event EventHandler<ValueEventArgs<MapViewRequest>>? ShowRequested;
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads the saved car. Call it once when the screen starts.
	/// </summary>
	/// <returns>A task that completes when the car was read.</returns>
	public async Task LoadAsync()
	{
		Car = await settings.GetCarAsync().ConfigureAwait(false);
		NotifyChanged();
	}

	/// <summary>
	/// Opens the dialog.
	/// </summary>
	public void Open()
	{
		IsOpen = true;
		NotifyChanged();
	}

	/// <summary>
	/// Closes the dialog.
	/// </summary>
	public void Close()
	{
		IsOpen = false;
		NotifyChanged();
	}

	/// <summary>
	/// Saves the car at the position or pin that is on screen, together with the headline the app showed.
	/// </summary>
	/// <returns>A task with <see langword="true"/> when the car was saved; <see langword="false"/> when there is no position yet.</returns>
	public async Task<bool> SaveHereAsync()
	{
		if (spot.CurrentPosition is not { } at)
		{
			toast.Show("No position yet: turn on my location or tap the map");
			return false;
		}

		var car = new ParkedCar(at, clock.GetLocalNow(), spot.LastHeadline, string.Empty);
		await settings.SaveCarAsync(car).ConfigureAwait(false);
		Car = car;
		NotifyChanged();
		toast.Show("Car position saved");
		return true;
	}

	/// <summary>
	/// Handles "I parked here" in the status block: saves the car and opens the dialog.
	/// </summary>
	/// <returns>A task that completes when the car was saved.</returns>
	public async Task ParkHereAsync()
	{
		if (await SaveHereAsync().ConfigureAwait(false))
		{
			Open();
		}
	}

	/// <summary>
	/// Opens Google Maps with walking directions to the car.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	public Task WalkToCarAsync(CancellationToken cancellationToken = default)
	{
		return Car is null ? Task.CompletedTask : launcher.OpenAsync(NavigationLinks.GoogleMapsWalking(Car.Point), cancellationToken);
	}

	/// <summary>
	/// Closes the dialog and asks the screen to show the car on the map.
	/// </summary>
	public void ShowOnMap()
	{
		if (Car is null)
		{
			return;
		}

		Close();
		ShowRequested?.Invoke(this, new ValueEventArgs<MapViewRequest>(new MapViewRequest(Car.Point, 18)));
	}

	/// <summary>
	/// Forgets the saved car.
	/// </summary>
	/// <returns>A task that completes when the car was forgotten.</returns>
	public async Task ForgetAsync()
	{
		await settings.ClearCarAsync().ConfigureAwait(false);
		Car = null;
		NotifyChanged();
	}
	#endregion
}
