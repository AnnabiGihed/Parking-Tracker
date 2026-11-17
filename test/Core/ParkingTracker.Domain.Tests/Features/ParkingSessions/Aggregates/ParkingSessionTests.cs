using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.ParkingSessions.Aggregates;
using ParkingTracker.Domain.Features.ParkingSessions.Errors;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Tests.Features.ParkingSessions.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingSession"/>: the position must be a place on Earth, a timer lasts 5 minutes to a
///              day and ends after the parking moment, a closed session no longer changes, and the bridges give back the car and
///              timer records they were created from.
/// </summary>
public sealed class ParkingSessionTests
{
	#region Static Instances
	/// <summary>
	/// The moment the car was parked.
	/// </summary>
	private static readonly DateTimeOffset Parked = new(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(1));

	/// <summary>
	/// A position in Brussels.
	/// </summary>
	private static readonly GeoPoint Place = new(50.8503, 4.3517);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Opens a session at the shared position and moment.
	/// </summary>
	/// <returns>The open session.</returns>
	private static ParkingSession Open() => ParkingSession.Create(Place, Parked, "Paid parking", "Rue Neuve 1").Value;
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that a new session is open, has no timer and raises no event.
	/// </summary>
	[Fact]
	public void Create_ValidPosition_ShouldOpenASessionWithoutTimer()
	{
		var result = ParkingSession.Create(Place, Parked, "Paid parking", "Rue Neuve 1");

		result.IsSuccess.Should().BeTrue();
		result.Value.IsOpen.Should().BeTrue();
		result.Value.Timer.Should().BeNull();
		result.Value.EndedAt.Should().BeNull();
		result.Value.GetDomainEvents().Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a position that is not a place on Earth is refused.
	/// </summary>
	/// <param name="latitude">The latitude.</param>
	/// <param name="longitude">The longitude.</param>
	[Theory]
	[InlineData(91, 4)]
	[InlineData(-91, 4)]
	[InlineData(50, 181)]
	[InlineData(50, -181)]
	[InlineData(double.NaN, 4)]
	[InlineData(50, double.PositiveInfinity)]
	public void Create_PositionNotOnEarth_ShouldFail(double latitude, double longitude)
	{
		var result = ParkingSession.Create(new GeoPoint(latitude, longitude), Parked, string.Empty, string.Empty);

		result.Error.Should().Be(ParkingSessionErrors.PositionInvalid);
	}

	/// <summary>
	/// Verifies that a missing text is a programming error.
	/// </summary>
	[Fact]
	public void Create_NullText_ShouldThrow()
	{
		var withoutHeadline = () => ParkingSession.Create(Place, Parked, null!, string.Empty);
		var withoutAddress = () => ParkingSession.Create(Place, Parked, string.Empty, null!);

		withoutHeadline.Should().Throw<ArgumentNullException>();
		withoutAddress.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Timer Tests
	/// <summary>
	/// Verifies that a timer within the limits is kept, and that a new one replaces it.
	/// </summary>
	[Fact]
	public void StartTimer_ValidTimer_ShouldKeepAndReplaceIt()
	{
		var session = Open();
		var first = ParkingTimer.StartingAt(60, Parked.AddMinutes(1));
		var second = ParkingTimer.StartingAt(30, Parked.AddMinutes(2));

		session.StartTimer(first).IsSuccess.Should().BeTrue();
		session.Timer.Should().Be(first);
		session.StartTimer(second).IsSuccess.Should().BeTrue();
		session.Timer.Should().Be(second);
	}

	/// <summary>
	/// Verifies that the limits of 5 and 1440 minutes are accepted and anything outside is refused.
	/// </summary>
	/// <param name="minutes">The length of the timer.</param>
	/// <param name="accepted">Whether the timer is accepted.</param>
	[Theory]
	[InlineData(5, true)]
	[InlineData(1440, true)]
	[InlineData(4, false)]
	[InlineData(0, false)]
	[InlineData(1441, false)]
	public void StartTimer_Length_ShouldBeLimited(int minutes, bool accepted)
	{
		var session = Open();

		var result = session.StartTimer(new ParkingTimer(Parked.AddMinutes(Math.Max(minutes, 1)), minutes));

		result.IsSuccess.Should().Be(accepted);
		if (!accepted)
		{
			result.Error.Should().Be(ParkingSessionErrors.TimerLengthOutOfRange);
			session.Timer.Should().BeNull();
		}
	}

	/// <summary>
	/// Verifies that a timer that ends at or before the parking moment is refused.
	/// </summary>
	/// <param name="minutesAfterParking">Where the end lies relative to the parking moment.</param>
	[Theory]
	[InlineData(0)]
	[InlineData(-30)]
	public void StartTimer_EndNotAfterParking_ShouldFail(int minutesAfterParking)
	{
		var result = Open().StartTimer(new ParkingTimer(Parked.AddMinutes(minutesAfterParking), 30));

		result.Error.Should().Be(ParkingSessionErrors.TimerEndsBeforeParking);
	}

	/// <summary>
	/// Verifies that stopping the timer removes it.
	/// </summary>
	[Fact]
	public void ClearTimer_RunningTimer_ShouldRemoveIt()
	{
		var session = Open();
		session.StartTimer(ParkingTimer.StartingAt(60, Parked));

		var result = session.ClearTimer();

		result.IsSuccess.Should().BeTrue();
		session.Timer.Should().BeNull();
	}
	#endregion

	#region End Tests
	/// <summary>
	/// Verifies that ending closes the session and stops the timer.
	/// </summary>
	[Fact]
	public void End_OpenSession_ShouldCloseItAndStopTheTimer()
	{
		var session = Open();
		session.StartTimer(ParkingTimer.StartingAt(60, Parked));
		var left = Parked.AddMinutes(20);

		var result = session.End(left);

		result.IsSuccess.Should().BeTrue();
		session.IsOpen.Should().BeFalse();
		session.EndedAt.Should().Be(left);
		session.Timer.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a closed session cannot end again, get a timer or lose its timer, as a conflict.
	/// </summary>
	[Fact]
	public void ClosedSession_FurtherChanges_ShouldFailWithConflict()
	{
		var session = Open();
		session.End(Parked.AddMinutes(5));

		var results = new[]
		{
			session.End(Parked.AddMinutes(6)),
			session.StartTimer(ParkingTimer.StartingAt(30, Parked)),
			session.ClearTimer(),
		};

		results.Should().OnlyContain(result => result.IsFailure && result.Error == ParkingSessionErrors.AlreadyEnded && result.ResultExceptionType == ResultExceptionType.Conflict);
		session.EndedAt.Should().Be(Parked.AddMinutes(5));
	}

	/// <summary>
	/// Verifies that a session cannot end before the car was parked.
	/// </summary>
	[Fact]
	public void End_BeforeParking_ShouldFail()
	{
		var session = Open();

		var result = session.End(Parked.AddMinutes(-1));

		result.Error.Should().Be(ParkingSessionErrors.EndedBeforeParked);
		session.IsOpen.Should().BeTrue();
	}
	#endregion

	#region Bridge Tests
	/// <summary>
	/// Verifies that a saved car and timer give a session that gives the same car and timer back.
	/// </summary>
	[Fact]
	public void FromParkedCar_CarAndTimer_ShouldRoundTrip()
	{
		var car = new ParkedCar(Place, Parked, "Paid parking", "Rue Neuve 1");
		var timer = ParkingTimer.StartingAt(90, Parked.AddMinutes(1));

		var result = ParkingSession.FromParkedCar(car, timer);

		result.IsSuccess.Should().BeTrue();
		result.Value.ToParkedCar().Should().Be(car);
		result.Value.Timer.Should().Be(timer);
	}

	/// <summary>
	/// Verifies that a saved car without timer gives a session without timer.
	/// </summary>
	[Fact]
	public void FromParkedCar_CarOnly_ShouldRoundTrip()
	{
		var car = new ParkedCar(Place, Parked, string.Empty, string.Empty);

		var result = ParkingSession.FromParkedCar(car, null);

		result.Value.ToParkedCar().Should().Be(car);
		result.Value.Timer.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a timer that breaks the rules makes the whole bridge fail with the rule's error.
	/// </summary>
	[Fact]
	public void FromParkedCar_TimerTooShort_ShouldFail()
	{
		var car = new ParkedCar(Place, Parked, string.Empty, string.Empty);

		var result = ParkingSession.FromParkedCar(car, new ParkingTimer(Parked.AddMinutes(1), 1));

		result.Error.Should().Be(ParkingSessionErrors.TimerLengthOutOfRange);
	}

	/// <summary>
	/// Verifies that a car at an impossible position fails the bridge, and that a missing car is a programming error.
	/// </summary>
	[Fact]
	public void FromParkedCar_BadArguments_ShouldFailOrThrow()
	{
		var nowhere = ParkingSession.FromParkedCar(new ParkedCar(new GeoPoint(200, 0), Parked, string.Empty, string.Empty), null);
		var withoutCar = () => ParkingSession.FromParkedCar(null!, null);

		nowhere.Error.Should().Be(ParkingSessionErrors.PositionInvalid);
		withoutCar.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Identifier Tests
	/// <summary>
	/// Verifies that an identifier is never empty and sorts like its value.
	/// </summary>
	[Fact]
	public void Id_Empty_ShouldBeRefusedAndOrderingFollowTheValue()
	{
		var empty = () => new ParkingSessionId(Guid.Empty);
		var low = new ParkingSessionId(new Guid(1, 0, 0, new byte[8]));
		var high = new ParkingSessionId(new Guid(2, 0, 0, new byte[8]));

		empty.Should().Throw<ArgumentException>();
		(low < high).Should().BeTrue();
		(low <= high).Should().BeTrue();
		(high > low).Should().BeTrue();
		(high >= low).Should().BeTrue();
	}
	#endregion
}
