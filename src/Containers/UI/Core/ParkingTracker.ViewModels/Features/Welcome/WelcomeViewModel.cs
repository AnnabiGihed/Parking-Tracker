using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.Welcome;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The welcome dialog shown on the first launch, when there is no card yet: it explains the app and the location
///              permission and leads to the card form. Once the user went on, it is not shown again.
/// </summary>
public sealed class WelcomeViewModel : ViewModelBase
{
	#region Fields
	/// <summary>
	/// Port to the saved settings.
	/// </summary>
	private readonly ISettingsStore settings;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="WelcomeViewModel"/> class.
	/// </summary>
	/// <param name="settings">Keeps whether the dialog was seen and gives the saved card.</param>
	/// <param name="permission">The location permission flow shown in the dialog.</param>
	public WelcomeViewModel(ISettingsStore settings, LocationPermissionViewModel permission)
	{
		this.settings = settings;
		Permission = permission;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets a value indicating whether the dialog is open.
	/// </summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets the location permission flow the dialog shows.
	/// </summary>
	public LocationPermissionViewModel Permission { get; }
	#endregion

	#region Events
	/// <summary>
	/// Occurs when the user chose to add the card, so that the card form opens.
	/// </summary>
	public event EventHandler? AddCardRequested;
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the dialog when the app starts without a card and the user has not seen it yet.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the permission state was read.</returns>
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (settings.GetCard() is not null || settings.GetWelcomeSeen())
		{
			return;
		}

		IsOpen = true;
		NotifyChanged();
		await Permission.RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Handles "Allow location all the time".
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the request was handled.</returns>
	public Task AllowLocationAlwaysAsync(CancellationToken cancellationToken = default)
	{
		return Permission.AllowAlwaysAsync(cancellationToken);
	}

	/// <summary>
	/// Handles "Add my parking card": closes the dialog for good and asks for the card form.
	/// </summary>
	public void AddCard()
	{
		Close();
		AddCardRequested?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Closes the dialog and remembers that it was seen.
	/// </summary>
	public void Close()
	{
		settings.SaveWelcomeSeen();
		IsOpen = false;
		NotifyChanged();
	}
	#endregion
}
