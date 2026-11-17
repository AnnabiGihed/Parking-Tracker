using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.ParkingSessions.Errors;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Primitives;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.ParkingSessions.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Where and when the user parked, what the app said then and the timer that guards the paid time, until the user
///              leaves the car. A timer lasts 5 minutes to one day and ends after the parking moment; a closed session no longer
///              changes. It bridges to and from the <see cref="ParkedCar"/> and <see cref="ParkingTimer"/> records the screens use.
/// </summary>
public sealed class ParkingSession : LightweightAggregateRoot<ParkingSessionId>
{
	#region Properties
	/// <summary>
	/// Gets where the car stands.
	/// </summary>
	public GeoPoint Position { get; }

	/// <summary>
	/// Gets when the position was saved.
	/// </summary>
	public DateTimeOffset ParkedAt { get; }

	/// <summary>
	/// Gets the headline the app showed at that moment, for example <c>Paid parking</c>; empty when unknown.
	/// </summary>
	public string Headline { get; }

	/// <summary>
	/// Gets where the car is in words (the <c>where</c> of the saved car); empty when unknown.
	/// </summary>
	public string AddressText { get; }

	/// <summary>
	/// Gets the running timer, or <see langword="null"/> when there is none.
	/// </summary>
	public ParkingTimer? Timer { get; private set; }

	/// <summary>
	/// Gets when the user left the car, or <see langword="null"/> while the session is open.
	/// </summary>
	public DateTimeOffset? EndedAt { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the session is still open.
	/// </summary>
	public bool IsOpen => EndedAt is null;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingSession"/> class.
	/// </summary>
	/// <param name="id">The identifier of the session.</param>
	/// <param name="position">Where the car stands.</param>
	/// <param name="parkedAt">When the position was saved.</param>
	/// <param name="headline">The headline the app showed.</param>
	/// <param name="addressText">Where the car is, in words.</param>
	private ParkingSession(ParkingSessionId id, GeoPoint position, DateTimeOffset parkedAt, string headline, string addressText)
		: base(id)
	{
		Position = position;
		ParkedAt = parkedAt;
		Headline = headline;
		AddressText = addressText;
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Opens a session for a car that was just parked.
	/// </summary>
	/// <param name="position">Where the car stands.</param>
	/// <param name="parkedAt">When the position was saved.</param>
	/// <param name="headline">The headline the app showed; empty when unknown.</param>
	/// <param name="addressText">Where the car is, in words; empty when unknown.</param>
	/// <returns>The open session, or a failure when the position is not a place on Earth.</returns>
	/// <exception cref="ArgumentNullException">A text is <see langword="null"/>.</exception>
	public static Result<ParkingSession> Create(GeoPoint position, DateTimeOffset parkedAt, string headline, string addressText)
	{
		ArgumentNullException.ThrowIfNull(headline);
		ArgumentNullException.ThrowIfNull(addressText);
		return IsOnEarth(position)
			? new ParkingSession(ParkingSessionId.New(), position, parkedAt, headline, addressText)
			: Result.Failure<ParkingSession>(ParkingSessionErrors.PositionInvalid);
	}

	/// <summary>
	/// Opens a session from the saved car and timer, the shapes the screens and the settings store use.
	/// </summary>
	/// <param name="car">The saved car.</param>
	/// <param name="timer">The saved timer, or <see langword="null"/> when there is none.</param>
	/// <returns>The open session, or the first rule the car or the timer breaks.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="car"/> is <see langword="null"/>.</exception>
	public static Result<ParkingSession> FromParkedCar(ParkedCar car, ParkingTimer? timer)
	{
		ArgumentNullException.ThrowIfNull(car);
		var created = Create(car.Point, car.Time, car.Status, car.Where);
		if (created.IsFailure || timer is null)
		{
			return created;
		}

		var started = created.Value.StartTimer(timer);
		return started.IsFailure ? Result.Failure<ParkingSession>(started.Error, started.ResultExceptionType) : created;
	}
	#endregion

	#region Domain Behaviours
	/// <summary>
	/// Starts a timer, replacing a running one.
	/// </summary>
	/// <param name="timer">The timer to run.</param>
	/// <returns>Success, or a failure when the session ended, the timer is shorter than 5 minutes or longer than one day, or it does not end after the parking moment.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="timer"/> is <see langword="null"/>.</exception>
	public Result StartTimer(ParkingTimer timer)
	{
		ArgumentNullException.ThrowIfNull(timer);
		if (!IsOpen)
		{
			return Result.Failure(ParkingSessionErrors.AlreadyEnded, ResultExceptionType.Conflict);
		}

		if (timer.Minutes is < ParkingTimer.MinimumMinutes or > ParkingTimer.MaximumMinutes)
		{
			return Result.Failure(ParkingSessionErrors.TimerLengthOutOfRange);
		}

		if (timer.End <= ParkedAt)
		{
			return Result.Failure(ParkingSessionErrors.TimerEndsBeforeParking);
		}

		Timer = timer;
		return Result.Success();
	}

	/// <summary>
	/// Stops the timer; nothing happens when none runs.
	/// </summary>
	/// <returns>Success, or a failure when the session ended.</returns>
	public Result ClearTimer()
	{
		if (!IsOpen)
		{
			return Result.Failure(ParkingSessionErrors.AlreadyEnded, ResultExceptionType.Conflict);
		}

		Timer = null;
		return Result.Success();
	}

	/// <summary>
	/// Closes the session because the user left the car; leaving also ends the parking time, so the timer stops.
	/// </summary>
	/// <param name="endedAt">When the user left the car.</param>
	/// <returns>Success, or a failure when the session ended already or <paramref name="endedAt"/> is before the parking moment.</returns>
	public Result End(DateTimeOffset endedAt)
	{
		if (!IsOpen)
		{
			return Result.Failure(ParkingSessionErrors.AlreadyEnded, ResultExceptionType.Conflict);
		}

		if (endedAt < ParkedAt)
		{
			return Result.Failure(ParkingSessionErrors.EndedBeforeParked);
		}

		EndedAt = endedAt;
		Timer = null;
		return Result.Success();
	}

	/// <summary>
	/// Gives the parked car in the shape the screens and the settings store use; it does not tell whether the session ended.
	/// </summary>
	/// <returns>A <see cref="ParkedCar"/> with the position, moment, headline and address text.</returns>
	public ParkedCar ToParkedCar() => new(Position, ParkedAt, Headline, AddressText);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether a position is a real place: finite, latitude within ±90 and longitude within ±180.
	/// </summary>
	/// <param name="position">The position to test.</param>
	/// <returns><see langword="true"/> when the position is a place on Earth.</returns>
	private static bool IsOnEarth(GeoPoint position)
	{
		return double.IsFinite(position.Latitude) && double.IsFinite(position.Longitude)
			&& Math.Abs(position.Latitude) <= 90 && Math.Abs(position.Longitude) <= 180;
	}
	#endregion
}
