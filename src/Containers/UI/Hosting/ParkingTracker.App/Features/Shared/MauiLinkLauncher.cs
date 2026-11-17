using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ILinkLauncher"/> of the phone over MAUI <c>Launcher</c>: Waze, Google Maps, payment pages and the e-mail
///              application open through Android intents. The manifest declares the <c>https</c> and <c>mailto</c> queries that
///              Android 11 and later need to see which application can answer.
/// </summary>
public sealed class MauiLinkLauncher : ILinkLauncher
{
	#region ILinkLauncher Implementation
	/// <inheritdoc />
	/// <remarks>The cancellation token is not used: starting an intent cannot be cancelled once the call is made.</remarks>
	public Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.TryOpenAsync(uri));
	}
	#endregion
}
