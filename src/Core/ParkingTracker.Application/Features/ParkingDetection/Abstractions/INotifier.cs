namespace ParkingTracker.Application.Features.ParkingDetection.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the notifications of the phone: asks the user for the permission to show them and shows the parking alert.
///              A tap on the alert opens the app. The permission only exists on recent versions of the phone system; on an older
///              one notifications are always allowed. The timer reminders are shown by <see cref="IAlarmScheduler"/>, which works
///              while the app is closed.
/// </summary>
public interface INotifier
{
	#region Methods
	/// <summary>
	/// Tells whether the app may show notifications now.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with <see langword="true"/> when notifications are allowed.</returns>
	Task<bool> AreAllowedAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Asks the user to allow notifications, unless that is already the case.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with <see langword="true"/> when notifications are allowed after the question.</returns>
	Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Shows one parking alert; each call shows a new notification, so that two parkings in a row both stay visible.
	/// </summary>
	/// <param name="title">The title, for example <c>Paid parking</c> with an icon in front.</param>
	/// <param name="body">The text under the title.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the notification was handed to the system.</returns>
	Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default);
	#endregion
}
