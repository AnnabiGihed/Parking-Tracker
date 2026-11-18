using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
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
///              shows it on the map, opens Google Maps with walking directions back to it, and forgets it. It also holds the parking
///              alerts switch (which starts and stops the background service) and the parking timer (presets, start, stop, the line
///              above the status block and the chip, which turns late 10 minutes before the end). The texts refresh every 30 seconds.
/// </summary>
public sealed class CarViewModel : ViewModelBase, IDisposable
{
	#region Constants
	/// <summary>
	/// The length the timer form starts with, in minutes.
	/// </summary>
	public const int DefaultMinutes = 120;
	#endregion

	#region Static Instances
	/// <summary>
	/// The preset lengths of the timer in minutes: 30 minutes, 1, 2 and 3 hours.
	/// </summary>
	public static readonly IReadOnlyList<int> Presets = [30, 60, 120, 180];

	/// <summary>
	/// How often the time left is written again.
	/// </summary>
	private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);
	#endregion

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

	/// <summary>
	/// Schedules and cancels the reminders of the timer.
	/// </summary>
	private readonly IAlarmScheduler alarms;

	/// <summary>
	/// Asks for the permission to show notifications.
	/// </summary>
	private readonly INotifier notifier;

	/// <summary>
	/// Starts and stops the background service that follows the alerts switch.
	/// </summary>
	private readonly IParkingAlertCoordinator alertCoordinator;

	/// <summary>
	/// Writes the time left again every 30 seconds.
	/// </summary>
	private readonly ITimer refreshTimer;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CarViewModel"/> class; the saved car is read by <see cref="LoadAsync"/>.
	/// </summary>
	/// <param name="settings">Keeps the car.</param>
	/// <param name="launcher">Opens the walking directions.</param>
	/// <param name="spot">The screen that knows where the user is.</param>
	/// <param name="toast">Shows the messages of the dialog.</param>
	/// <param name="clock">Clock that stamps the saved position and measures the timer.</param>
	/// <param name="alarms">Schedules and cancels the reminders of the timer.</param>
	/// <param name="notifier">Asks for the permission to show notifications.</param>
	/// <param name="alertCoordinator">Starts and stops the background service that follows the alerts switch.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public CarViewModel(ISettingsStore settings, ILinkLauncher launcher, SpotCheckViewModel spot, ToastViewModel toast, TimeProvider clock, IAlarmScheduler alarms, INotifier notifier, IParkingAlertCoordinator alertCoordinator)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(launcher);
		ArgumentNullException.ThrowIfNull(spot);
		ArgumentNullException.ThrowIfNull(toast);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(alarms);
		ArgumentNullException.ThrowIfNull(notifier);
		ArgumentNullException.ThrowIfNull(alertCoordinator);
		this.settings = settings;
		this.launcher = launcher;
		this.spot = spot;
		this.toast = toast;
		this.clock = clock;
		this.alarms = alarms;
		this.notifier = notifier;
		this.alertCoordinator = alertCoordinator;
		refreshTimer = clock.CreateTimer(_ => NotifyChanged(), null, RefreshEvery, RefreshEvery);
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the saved car, or <see langword="null"/> when none is saved.
	/// </summary>
	public ParkedCar? Car { get; private set; }

	/// <summary>
	/// Gets the running parking timer, or <see langword="null"/> when none runs.
	/// </summary>
	public ParkingTimer? Timer { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the parking alerts are on; they are on by default.
	/// </summary>
	public bool AlertsEnabled { get; private set; } = true;

	/// <summary>
	/// Gets the length in minutes that the timer form holds.
	/// </summary>
	public int Minutes { get; private set; } = DefaultMinutes;

	/// <summary>
	/// Gets the line above the status block, or <see langword="null"/> when no timer runs.
	/// </summary>
	public string? TimerLine => Timer is null ? null : TimerText.Line(Timer, clock.GetLocalNow());

	/// <summary>
	/// Gets the text of the car chip: the time left while a timer runs, otherwise "My car".
	/// </summary>
	public string ChipText => Timer is null ? "My car" : TimerText.Chip(Timer, clock.GetLocalNow());

	/// <summary>
	/// Gets a value indicating whether the chip is shown: while a car is saved or a timer runs.
	/// </summary>
	public bool ChipVisible => Car is not null || Timer is not null;

	/// <summary>
	/// Gets a value indicating whether the chip shows a timer that has 10 minutes or less left.
	/// </summary>
	public bool ChipIsLate => Timer is { } timer && timer.IsLate(clock.GetLocalNow());

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
	/// Reads the saved car, timer and alerts switch. Call it when the screen starts and when the user comes back to the app, because
	/// the background service saves a parked car while the app is away.
	/// </summary>
	/// <returns>A task that completes when the values were read.</returns>
	public async Task LoadAsync()
	{
		Car = await settings.GetCarOrNoneAsync().ConfigureAwait(false);
		Timer = await settings.GetTimerOrNoneAsync().ConfigureAwait(false);
		AlertsEnabled = await settings.GetAlertsEnabledAsync().ConfigureAwait(false);
		NotifyChanged();
	}

	/// <summary>
	/// Opens the dialog. While no timer runs and the last answer knows the maximum stay of the zone, the timer form starts with that
	/// length, as the first version did, so that one tap on start covers the time the zone allows.
	/// </summary>
	public void Open()
	{
		if (Timer is null && spot.LastMaxStayMinutes is { } maximum)
		{
			Minutes = maximum;
		}

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
	/// Forgets the saved car and stops the timer, as leaving the spot ends the parking time.
	/// </summary>
	/// <returns>A task that completes when the car was forgotten.</returns>
	public async Task ForgetAsync()
	{
		await settings.ClearCarAsync().ConfigureAwait(false);
		Car = null;
		await StopTimerAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Switches the parking alerts on or off and starts or stops the background service to match. Switching them on also asks for
	/// the permission to show notifications and, when the service cannot run yet, says that the location must be allowed all the time.
	/// </summary>
	/// <param name="enabled">Whether the alerts are on.</param>
	/// <returns>A task that completes when the switch is saved and the service follows it.</returns>
	public async Task SetAlertsAsync(bool enabled)
	{
		await settings.SaveAlertsEnabledAsync(enabled).ConfigureAwait(false);
		AlertsEnabled = enabled;
		NotifyChanged();
		if (enabled)
		{
			await notifier.RequestPermissionAsync().ConfigureAwait(false);
		}

		var running = await alertCoordinator.ApplyAsync().ConfigureAwait(false);
		if (enabled && !running)
		{
			toast.Show("Allow location \u201Call the time\u201D so alerts also work in the background");
		}
	}

	/// <summary>
	/// Sets the length that the timer form holds, for example from a preset button or the minutes field.
	/// </summary>
	/// <param name="minutes">The length in minutes; it is limited only when the timer starts.</param>
	public void SetMinutes(int minutes)
	{
		Minutes = minutes;
		NotifyChanged();
	}

	/// <summary>
	/// Starts a timer of the length the form holds, from now.
	/// </summary>
	/// <returns>A task with <see langword="true"/> when the timer started.</returns>
	public Task<bool> StartTimerAsync() => ApplyTimerAsync(ParkingTimer.StartingAt(Minutes, clock.GetLocalNow()));

	/// <summary>
	/// Starts the given timer: asks for the permission to notify, replaces the scheduled reminders, saves the timer and tells the
	/// user. When the phone only allows inexact alarms the user is told and the system screen for exact alarms opens.
	/// </summary>
	/// <param name="timer">The timer to run.</param>
	/// <returns>A task with <see langword="true"/> when the timer started; <see langword="false"/> when notifications were refused.</returns>
	public async Task<bool> ApplyTimerAsync(ParkingTimer timer)
	{
		ArgumentNullException.ThrowIfNull(timer);
		if (!await notifier.RequestPermissionAsync().ConfigureAwait(false))
		{
			toast.Show("Allow notifications for the reminder.");
			return false;
		}

		await alarms.CancelAsync().ConfigureAwait(false);
		var precision = await alarms.ScheduleAsync(timer.Plan(clock.GetLocalNow())).ConfigureAwait(false);
		await settings.SaveTimerAsync(timer).ConfigureAwait(false);
		Timer = timer;
		NotifyChanged();
		var message = $"Timer set: {TimerText.Length(timer.Minutes)}";
		if (precision == AlarmPrecision.Inexact)
		{
			toast.Show(message + ". Allow exact alarms so the reminders come on time.");
			await alarms.OpenExactAlarmSettingsAsync().ConfigureAwait(false);
			return true;
		}

		toast.Show(message);
		return true;
	}

	/// <summary>
	/// Stops the timer: forgets it and cancels its reminders.
	/// </summary>
	/// <returns>A task that completes when the timer is stopped.</returns>
	public async Task StopTimerAsync()
	{
		await settings.ClearTimerAsync().ConfigureAwait(false);
		await alarms.CancelAsync().ConfigureAwait(false);
		Timer = null;
		NotifyChanged();
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		refreshTimer.Dispose();
	}
	#endregion
}
