using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The location permission flow shared by the banner in the bottom sheet and the welcome dialog: reads what the user
///              allowed, decides which banner to show, asks for the foreground permission and sends the user to the settings of
///              the app for "Allow all the time", which Android only offers there. "Later" postpones the banner for a week.
/// </summary>
public sealed class LocationPermissionViewModel : ViewModelBase
{
	#region Static Instances
	/// <summary>
	/// How long "Later" hides the "all the time" banner.
	/// </summary>
	public static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(7);
	#endregion

	#region Fields
	/// <summary>
	/// Port to the permissions of the device.
	/// </summary>
	private readonly ILocationPermissions permissions;

	/// <summary>
	/// Port to the saved settings, which keep the postponement.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Clock that decides whether the postponement is over.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// Shows the messages of the flow.
	/// </summary>
	private readonly ToastViewModel toast;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LocationPermissionViewModel"/> class.
	/// </summary>
	/// <param name="permissions">Reads and asks the permissions.</param>
	/// <param name="settings">Keeps the postponement of the banner.</param>
	/// <param name="clock">Clock that decides whether the postponement is over.</param>
	/// <param name="toast">Shows the messages of the flow.</param>
	public LocationPermissionViewModel(ILocationPermissions permissions, ISettingsStore settings, TimeProvider clock, ToastViewModel toast)
	{
		this.permissions = permissions;
		this.settings = settings;
		this.clock = clock;
		this.toast = toast;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets what the user allowed as of the last <see cref="RefreshAsync"/>; nothing is known before the first one.
	/// </summary>
	public LocationPermissionStatus Status { get; private set; } = new(PermissionState.Unknown, PermissionState.Unknown);

	/// <summary>
	/// Gets the banner to show in the bottom sheet.
	/// </summary>
	public PermissionBannerKind Banner { get; private set; }

	/// <summary>
	/// Gets the sentence under the button of the welcome dialog; empty while nothing is allowed.
	/// </summary>
	public string WelcomeMessage { get; private set; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether the welcome dialog offers the "Allow location all the time" button.
	/// </summary>
	public bool ShowAllowAlwaysButton => Status.Background != PermissionState.Granted;
	#endregion

	#region Events
	/// <summary>
	/// Occurs when the user allowed the position while the app is in use, so that the tracking can start.
	/// </summary>
	public event EventHandler? ForegroundGranted;
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads the permissions again and updates the banner and the welcome sentence. Call it when the app opens and when the
	/// user comes back from the settings of the phone.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the state was updated.</returns>
	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		Status = await permissions.GetStatusAsync(cancellationToken).ConfigureAwait(false);
		Banner = BannerFor(Status, settings.GetPermissionSnoozeUntil(), clock.GetUtcNow());
		WelcomeMessage = WelcomeMessageFor(Status);
		NotifyChanged();
	}

	/// <summary>
	/// Handles "Turn on" of the banner: asks for the permission when it is off, otherwise opens the settings of the app.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the request was handled.</returns>
	public async Task TurnOnAsync(CancellationToken cancellationToken = default)
	{
		if (Status.Foreground == PermissionState.Denied)
		{
			await RequestForegroundAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		await permissions.OpenSettingsAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Handles "Allow" and "Allow location all the time": asks for the foreground permission first, then sends the user to
	/// the settings of the app where "Allow all the time" can be chosen.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the request was handled.</returns>
	public async Task AllowAlwaysAsync(CancellationToken cancellationToken = default)
	{
		var current = await permissions.GetStatusAsync(cancellationToken).ConfigureAwait(false);
		if (current.Foreground != PermissionState.Granted)
		{
			await RequestForegroundAsync(cancellationToken).ConfigureAwait(false);
			toast.Show("Allow location, then choose “All the time” in the next step");
			return;
		}

		await permissions.OpenSettingsAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Handles "Later": hides the "all the time" banner for a week.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the banner was updated.</returns>
	public Task PostponeAsync(CancellationToken cancellationToken = default)
	{
		settings.SavePermissionSnoozeUntil(clock.GetUtcNow() + SnoozeDuration);
		return RefreshAsync(cancellationToken);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Asks for the foreground permission and, when it is given, lets the tracking start and updates the state.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the state was updated.</returns>
	private async Task RequestForegroundAsync(CancellationToken cancellationToken)
	{
		var granted = await permissions.RequestForegroundAsync(cancellationToken).ConfigureAwait(false);
		if (granted)
		{
			ForegroundGranted?.Invoke(this, EventArgs.Empty);
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Writes the sentence under the button of the welcome dialog.
	/// </summary>
	/// <param name="status">What the user allowed.</param>
	/// <returns>The sentence; empty while nothing is allowed.</returns>
	private static string WelcomeMessageFor(LocationPermissionStatus status)
	{
		if (status.Background == PermissionState.Granted)
		{
			return "✓ Location allowed all the time.";
		}

		return status.Foreground == PermissionState.Granted ? "✓ Allowed while using the app. Tap the button to allow it all the time." : string.Empty;
	}

	/// <summary>
	/// Decides which banner fits the permissions.
	/// </summary>
	/// <param name="status">What the user allowed.</param>
	/// <param name="snoozedUntil">Until when the "all the time" banner was postponed, or <see langword="null"/>.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>The banner.</returns>
	private static PermissionBannerKind BannerFor(LocationPermissionStatus status, DateTimeOffset? snoozedUntil, DateTimeOffset now)
	{
		if (status.Foreground == PermissionState.Denied)
		{
			return PermissionBannerKind.TurnOn;
		}

		var snoozed = snoozedUntil is { } until && now < until;
		return status.Background == PermissionState.Denied && !snoozed ? PermissionBannerKind.AllowAlways : PermissionBannerKind.None;
	}
	#endregion
}
