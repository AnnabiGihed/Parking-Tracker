using Android.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="IParkingWatchService"/> of the phone: starts and stops <see cref="ParkingTrackerLocationService"/> as a
///              foreground service. Since Android 12 the system refuses to start one from the background; that refusal is logged and
///              the service simply stays off until the user opens the app again, which asks once more.
/// </summary>
/// <remarks>
/// This class depends on Android, so it cannot be unit-tested on a PC; the emulator test covers it (docs/architecture.md, section 6.3).
/// </remarks>
public sealed partial class AndroidParkingWatchService : IParkingWatchService
{
	#region Fields
	/// <summary>
	/// The log, where a refused start goes.
	/// </summary>
	private readonly ILogger<AndroidParkingWatchService> logger;

	/// <summary>
	/// Tells whether the service exists.
	/// </summary>
	private readonly ParkingServiceState state;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AndroidParkingWatchService"/> class.
	/// </summary>
	/// <param name="logger">The log, where a refused start goes.</param>
	/// <param name="state">Tells whether the service exists.</param>
	public AndroidParkingWatchService(ILogger<AndroidParkingWatchService> logger, ParkingServiceState state)
	{
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(state);
		this.logger = logger;
		this.state = state;
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsRunning => state.IsRunning;
	#endregion

	#region IParkingWatchService Implementation
	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		var context = Platform.AppContext;
		var intent = new Intent(context, typeof(ParkingTrackerLocationService));
		try
		{
			if (OperatingSystem.IsAndroidVersionAtLeast(26))
			{
				context.StartForegroundService(intent);
			}
			else
			{
				context.StartService(intent);
			}
		}
		catch (Exception exception)
		{
			LogStartRefused(logger, exception);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		var context = Platform.AppContext;
		context.StopService(new Intent(context, typeof(ParkingTrackerLocationService)));
		return Task.CompletedTask;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that the system refused to start the service.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">The cause.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The parking alert service could not be started")]
	private static partial void LogStartRefused(ILogger logger, Exception exception);
	#endregion
}
