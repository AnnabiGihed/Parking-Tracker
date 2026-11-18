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

	/// <summary>
	/// Rebuilds a session that was stored before, with its stored identifier and the rules of the behaviours: the position is on
	/// Earth, a timer lasts 5 minutes to one day and ends after the parking moment, and a session that ended neither ended before it
	/// was parked nor still runs a timer. It raises no event: the session existed already.
	/// </summary>
	/// <param name="id">The stored identifier of the session.</param>
	/// <param name="position">Where the car stands.</param>
	/// <param name="parkedAt">When the position was saved.</param>
	/// <param name="headline">The headline the app showed; empty when unknown.</param>
	/// <param name="addressText">Where the car is, in words; empty when unknown.</param>
	/// <param name="timer">The stored timer, or <see langword="null"/> when none runs.</param>
	/// <param name="endedAt">When the user left the car, or <see langword="null"/> while the session is open.</param>
	/// <returns>The session, or the first rule the stored values break.</returns>
	/// <exception cref="ArgumentNullException">The identifier or a text is <see langword="null"/>.</exception>
	public static Result<ParkingSession> Restore(ParkingSessionId id, GeoPoint position, DateTimeOffset parkedAt, string headline, string addressText, ParkingTimer? timer, DateTimeOffset? endedAt)
	{
		ArgumentNullException.ThrowIfNull(id);
		ArgumentNullException.ThrowIfNull(headline);
		ArgumentNullException.ThrowIfNull(addressText);
		if (!IsOnEarth(position))
		{
			return Result.Failure<ParkingSession>(ParkingSessionErrors.PositionInvalid);
		}

		var check = timer is null ? Result.Success() : CheckTimer(timer, parkedAt);
		if (check.IsSuccess && endedAt is { } ended)
		{
			check = timer is not null ? Result.Failure(ParkingSessionErrors.AlreadyEnded, ResultExceptionType.Conflict) : CheckEnd(ended, parkedAt);
		}

		return check.IsFailure
			? Result.Failure<ParkingSession>(check.Error, check.ResultExceptionType)
			: new ParkingSession(id, position, parkedAt, headline, addressText) { Timer = timer, EndedAt = endedAt };
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

		var check = CheckTimer(timer, ParkedAt);
		if (check.IsFailure)
		{
			return check;
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

		var check = CheckEnd(endedAt, ParkedAt);
		if (check.IsFailure)
		{
			return check;
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
	/// Checks that a timer lasts 5 minutes to one day and ends after the parking moment.
	/// </summary>
	/// <param name="timer">The timer to check.</param>
	/// <param name="parkedAt">When the car was parked.</param>
	/// <returns>Success, or the rule the timer breaks.</returns>
	private static Result CheckTimer(ParkingTimer timer, DateTimeOffset parkedAt)
	{
		if (timer.Minutes is < ParkingTimer.MinimumMinutes or > ParkingTimer.MaximumMinutes)
		{
			return Result.Failure(ParkingSessionErrors.TimerLengthOutOfRange);
		}

		return timer.End <= parkedAt ? Result.Failure(ParkingSessionErrors.TimerEndsBeforeParking) : Result.Success();
	}

	/// <summary>
	/// Checks that a session does not end before the car was parked.
	/// </summary>
	/// <param name="endedAt">When the user left the car.</param>
	/// <param name="parkedAt">When the car was parked.</param>
	/// <returns>Success, or the failure for an end before the parking moment.</returns>
	private static Result CheckEnd(DateTimeOffset endedAt, DateTimeOffset parkedAt)
	{
		return endedAt < parkedAt ? Result.Failure(ParkingSessionErrors.EndedBeforeParked) : Result.Success();
	}

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
