using Microsoft.Maui.Networking;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.App.Features.SpotDecisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="INetworkStatus"/> of the phone over MAUI <c>Connectivity</c>. It also reports a change between online and
///              offline, which the screen answers with "can't verify" or a new check; a change that keeps the same state is not reported.
///              A connection that only reaches a captive portal or a local network counts as offline, as the app needs the real internet.
/// </summary>
public sealed class ConnectivityNetworkStatus : INetworkStatus, IDisposable
{
	#region Fields
	/// <summary>
	/// Guards <see cref="lastOnline"/>; connectivity changes arrive on any thread.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The state that was last reported, to skip repeats.
	/// </summary>
	private bool lastOnline;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ConnectivityNetworkStatus"/> class and starts listening for changes.
	/// </summary>
	public ConnectivityNetworkStatus()
	{
		lastOnline = IsOnline;
		Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
	#endregion

	#region Events
	/// <summary>
	/// Occurs, possibly on any thread, when the device went online or offline; the flag is <see langword="true"/> when it is online now.
	/// </summary>
	public event EventHandler<ValueEventArgs<bool>>? OnlineChanged;
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reports a change of the online state.
	/// </summary>
	/// <param name="sender">The connectivity service.</param>
	/// <param name="e">The new network access.</param>
	private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
	{
		var online = e.NetworkAccess == NetworkAccess.Internet;
		lock (gate)
		{
			if (online == lastOnline)
			{
				return;
			}

			lastOnline = online;
		}

		OnlineChanged?.Invoke(this, new ValueEventArgs<bool>(online));
	}
	#endregion
}
