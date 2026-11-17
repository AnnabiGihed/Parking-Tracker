using AndroidX.Core.App;
using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="INotifier"/> of the phone over <c>NotificationCompat</c> (ADR-0002). Alerts go to their own channel and a
///              tap opens the app. Before Android 13 notifications need no question, so asking only reads whether the user switched
///              them off in the settings; from Android 13 the runtime permission is asked in the system dialog, on the main thread.
/// </summary>
/// <remarks>
/// This class depends on Android, so it cannot be unit-tested on a PC; the emulator test covers it (docs/architecture.md, section 6.3).
/// </remarks>
public sealed class AndroidNotifier : INotifier
{
	#region Fields
	/// <summary>
	/// The clock whose milliseconds make the id of each alert different.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AndroidNotifier"/> class.
	/// </summary>
	/// <param name="clock">The clock whose milliseconds make the id of each alert different.</param>
	public AndroidNotifier(TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(clock);
		this.clock = clock;
	}
	#endregion

	#region INotifier Implementation
	/// <inheritdoc />
	public Task<bool> AreAllowedAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(NotificationManagerCompat.From(Platform.AppContext)!.AreNotificationsEnabled());
	}

	/// <inheritdoc />
	public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
	{
		if (await AreAllowedAsync(cancellationToken).ConfigureAwait(false))
		{
			return true;
		}

		// Before Android 13 there is nothing to ask: the user switched notifications off in the settings.
		if (!OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			return false;
		}

		var status = await MainThread.InvokeOnMainThreadAsync(() => new PostNotificationsPermission().RequestAsync()).ConfigureAwait(false);
		return status == PermissionStatus.Granted && await AreAllowedAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default)
	{
		var id = ParkingNotifications.AlertIdBase + (int)(clock.GetUtcNow().ToUnixTimeMilliseconds() % 1000);
		ParkingNotifications.Show(Platform.AppContext, id, ParkingNotifications.AlertsChannelId, title, body);
		return Task.CompletedTask;
	}
	#endregion
}
