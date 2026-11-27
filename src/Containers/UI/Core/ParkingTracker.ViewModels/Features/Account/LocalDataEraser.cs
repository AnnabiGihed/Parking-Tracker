using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Vehicles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Removes the data of an account from the phone and makes the screens show it (ADR-0032). The wipe itself is the
///              transaction of <see cref="IDeviceDataWiper"/>; afterwards the stores forget what they remembered
///              (<see cref="DeviceDataResetNotice"/>, the same notice as after a lost key) and the view models read their data again
///              through the loads they already have, which raise their own <c>Changed</c> events, so that the chips, the favourites, the
///              vehicle, the card line and the parked car show empty without a restart. A full wipe removes the parking session, so the
///              reminder alarms of its timer are cancelled too, once the wipe is committed. Nothing is read again after a failed wipe,
///              because nothing was removed.
/// </summary>
public sealed partial class LocalDataEraser
{
	#region Fields
	/// <summary>
	/// Removes the rows.
	/// </summary>
	private readonly IDeviceDataWiper wiper;

	/// <summary>
	/// Tells the stores to forget what they remembered.
	/// </summary>
	private readonly DeviceDataResetNotice resetNotice;

	/// <summary>
	/// The saved places that the chips and the favourites show.
	/// </summary>
	private readonly PlacesViewModel places;

	/// <summary>
	/// The vehicle profile.
	/// </summary>
	private readonly VehicleViewModel vehicle;

	/// <summary>
	/// The residents' card and its line.
	/// </summary>
	private readonly CardViewModel card;

	/// <summary>
	/// The parked car and its timer.
	/// </summary>
	private readonly CarViewModel car;

	/// <summary>
	/// Cancels the reminders of the timer of a parking session that was removed.
	/// </summary>
	private readonly IAlarmScheduler alarms;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<LocalDataEraser> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LocalDataEraser"/> class.
	/// </summary>
	/// <param name="wiper">Removes the rows.</param>
	/// <param name="resetNotice">Tells the stores to forget what they remembered.</param>
	/// <param name="places">The saved places that the chips and the favourites show.</param>
	/// <param name="vehicle">The vehicle profile.</param>
	/// <param name="card">The residents' card and its line.</param>
	/// <param name="car">The parked car and its timer.</param>
	/// <param name="alarms">Cancels the reminders of the timer of a parking session that was removed.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public LocalDataEraser(IDeviceDataWiper wiper, DeviceDataResetNotice resetNotice, PlacesViewModel places, VehicleViewModel vehicle, CardViewModel card, CarViewModel car, IAlarmScheduler alarms, ILogger<LocalDataEraser> logger)
	{
		ArgumentNullException.ThrowIfNull(wiper);
		ArgumentNullException.ThrowIfNull(resetNotice);
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(vehicle);
		ArgumentNullException.ThrowIfNull(card);
		ArgumentNullException.ThrowIfNull(car);
		ArgumentNullException.ThrowIfNull(alarms);
		ArgumentNullException.ThrowIfNull(logger);
		this.wiper = wiper;
		this.resetNotice = resetNotice;
		this.places = places;
		this.vehicle = vehicle;
		this.card = card;
		this.car = car;
		this.alarms = alarms;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a reminder alarm that could not be cancelled after the wipe.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the scheduler reported.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The reminder alarms could not be cancelled after the data of the account was removed")]
	private static partial void LogAlarmCancelFailed(ILogger logger, Exception exception);
	#endregion

	#region Public Methods
	/// <summary>
	/// Removes the places, the vehicle and the card, and keeps the parked car.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wipe before it starts.</param>
	/// <returns>A task with success, or the failure of the wipe.</returns>
	public async Task<Result> WipeProfileAsync(CancellationToken cancellationToken = default)
	{
		var result = await wiper.WipeProfileAsync(cancellationToken).ConfigureAwait(false);
		return await ShowAsync(result).ConfigureAwait(false);
	}

	/// <summary>
	/// Removes everything: the profile, the parked car and the owner binding.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wipe before it starts.</param>
	/// <returns>A task with success, or the failure of the wipe.</returns>
	public async Task<Result> WipeAllAsync(CancellationToken cancellationToken = default)
	{
		var result = await wiper.WipeAllAsync(cancellationToken).ConfigureAwait(false);
		if (result.IsSuccess)
		{
			await CancelAlarmsAsync(cancellationToken).ConfigureAwait(false);
		}

		return await ShowAsync(result).ConfigureAwait(false);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Cancels the reminder alarms of the removed session; a failure is logged and does not stop the screens from reloading, because
	/// the data is gone either way.
	/// </summary>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task that completes when the alarms were cancelled or the failure was logged.</returns>
	private async Task CancelAlarmsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await alarms.CancelAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogAlarmCancelFailed(logger, exception);
		}
	}

	/// <summary>
	/// Makes the screens read the empty database after a wipe that succeeded.
	/// </summary>
	/// <param name="result">The result of the wipe.</param>
	/// <returns>A task with the same result.</returns>
	private async Task<Result> ShowAsync(Result result)
	{
		if (result.IsFailure)
		{
			return result;
		}

		resetNotice.Raise();
		await places.LoadAsync().ConfigureAwait(false);
		await vehicle.LoadAsync().ConfigureAwait(false);
		await card.RefreshLineAsync().ConfigureAwait(false);
		await car.LoadAsync().ConfigureAwait(false);
		return result;
	}
	#endregion
}
