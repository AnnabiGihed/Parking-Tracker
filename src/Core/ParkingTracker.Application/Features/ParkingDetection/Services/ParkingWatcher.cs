using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;
using ParkingTracker.Domain.Features.ParkingDetection.Services;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.Application.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Joins the pure <see cref="ParkingDetector"/> to the mediator for the background service: the service passes every
///              fix to <see cref="Observe"/> and runs <see cref="RunAsync"/>, which looks every 15 seconds whether the user has
///              parked; when so it sends a <see cref="HandleParkedCommand"/>. The service lives outside the scope of the web view,
///              so the command is sent from a scope of its own, created from the root provider. A failure of one look is logged
///              and the next look goes on.
/// </summary>
public sealed partial class ParkingWatcher
{
	#region Static Instances
	/// <summary>
	/// The time between two looks at the detector.
	/// </summary>
	public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(15);
	#endregion

	#region Fields
	/// <summary>
	/// Creates the scope that the command is sent in.
	/// </summary>
	private readonly IServiceScopeFactory scopes;

	/// <summary>
	/// Clock that gives the moment of each look.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log, where a failed look goes.
	/// </summary>
	private readonly ILogger<ParkingWatcher> logger;

	/// <summary>
	/// Guards <see cref="detector"/>, which the location callback and the look touch from different threads.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The detector, replaced by <see cref="Reset"/>.
	/// </summary>
	private ParkingDetector detector = new();
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingWatcher"/> class.
	/// </summary>
	/// <param name="scopes">Creates the scope that the command is sent in.</param>
	/// <param name="clock">Clock that gives the moment of each look.</param>
	/// <param name="logger">The log, where a failed look goes.</param>
	public ParkingWatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ParkingWatcher> logger)
	{
		ArgumentNullException.ThrowIfNull(scopes);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.scopes = scopes;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Passes a fix of the device to the detector.
	/// </summary>
	/// <param name="fix">The fix.</param>
	public void Observe(GpsFix fix)
	{
		lock (gate)
		{
			detector.Observe(fix);
		}
	}

	/// <summary>
	/// Forgets everything seen so far, for example when the service starts again.
	/// </summary>
	public void Reset()
	{
		lock (gate)
		{
			detector = new ParkingDetector();
		}
	}

	/// <summary>
	/// Looks whether the user has parked and, when so, handles it once.
	/// </summary>
	/// <param name="cancellationToken">Cancels the handling.</param>
	/// <returns>A task with <see langword="true"/> when parking was found and handled.</returns>
	public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
	{
		GpsFix? parked;
		lock (gate)
		{
			parked = detector.GetParkedFix(clock.GetUtcNow());
			if (parked is null)
			{
				return false;
			}

			// Acknowledged before the handling, so that a slow answer cannot announce the same stop twice.
			detector.AcknowledgeParked();
		}

		using var scope = scopes.CreateScope();
		var sender = scope.ServiceProvider.GetRequiredService<ISender>();
		var command = new HandleParkedCommand(parked.Point.Latitude, parked.Point.Longitude, parked.AccuracyMetres);
		var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			LogHandlingFailed(logger, result.Error.Message);
		}

		return result.IsSuccess;
	}

	/// <summary>
	/// Looks every <see cref="CheckEvery"/> until cancelled; an exception of one look is logged and does not end the loop.
	/// </summary>
	/// <param name="cancellationToken">Ends the loop.</param>
	/// <returns>A task that completes when cancelled.</returns>
	public async Task RunAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(CheckEvery, clock);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				await CheckSafelyAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// Cancelling is how the service ends the loop.
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Runs one look and logs what goes wrong, so that the loop survives it.
	/// </summary>
	/// <param name="cancellationToken">Cancels the look.</param>
	/// <returns>A task that completes when the look ended.</returns>
	private async Task CheckSafelyAsync(CancellationToken cancellationToken)
	{
		try
		{
			await CheckAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			LogCheckFailed(logger, exception);
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that handling a stop failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="message">Why it failed.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Handling the parking failed: {Message}")]
	private static partial void LogHandlingFailed(ILogger logger, string message);

	/// <summary>
	/// Logs that a look at the detector threw.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Looking for a parking failed")]
	private static partial void LogCheckFailed(ILogger logger, Exception exception);
	#endregion
}
