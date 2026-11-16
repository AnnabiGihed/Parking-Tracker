using ParkingTracker.Domain.Cards;
using ParkingTracker.Domain.Tests.TestDoubles;

namespace ParkingTracker.Domain.Tests.Cards;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CardState"/>: <c>cardState</c> of live.js, with today fixed to 16 November 2026.
/// </summary>
public sealed class CardStateTests
{
	#region Test Infrastructure
	private static readonly FixedTimeProvider Today = new(RuleBuilder.At("2026-11-16T12:00:00Z"));
	#endregion

	#region Evaluate Tests
	/// <summary>
	/// Verifies that no card gives an invalid state that tells the user to set one up.
	/// </summary>
	[Fact]
	public void Evaluate_NoCard_ShouldBeInvalidWithSetupMessage()
	{
		var state = CardState.Evaluate(null, Today);

		state.IsValid.Should().BeFalse();
		state.Reason.Should().Be("No parking card set up yet — tap 🪪.");
	}

	/// <summary>
	/// Verifies that a card that starts later is not valid yet, with the date as day/month/year.
	/// </summary>
	[Fact]
	public void Evaluate_CardStartingTomorrow_ShouldBeNotYetValid()
	{
		var state = CardState.Evaluate(new CardPeriod(new DateOnly(2026, 11, 17), null), Today);

		state.IsValid.Should().BeFalse();
		state.Reason.Should().Be("Your card is only valid from 17/11/2026.");
	}

	/// <summary>
	/// Verifies that a card that ended yesterday has expired.
	/// </summary>
	[Fact]
	public void Evaluate_CardEndedYesterday_ShouldBeExpired()
	{
		var state = CardState.Evaluate(new CardPeriod(null, new DateOnly(2026, 11, 15)), Today);

		state.IsValid.Should().BeFalse();
		state.Reason.Should().Be("Your card expired on 15/11/2026.");
	}

	/// <summary>
	/// Verifies that the first and the last day are valid, and a card without dates is always valid.
	/// </summary>
	[Fact]
	public void Evaluate_TodayOnTheEdgesOrNoDates_ShouldBeValid()
	{
		var onTheEdges = new CardPeriod(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16));

		CardState.Evaluate(onTheEdges, Today).Should().Be(new CardState(true, null));
		CardState.Evaluate(new CardPeriod(null, null), Today).Should().Be(new CardState(true, null));
	}

	/// <summary>
	/// Verifies that today is the date on the local clock: 23:30 UTC is already the next day in Brussels.
	/// </summary>
	[Fact]
	public void Evaluate_LocalZoneAheadOfUtc_ShouldUseTheLocalDate()
	{
		var brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
		var late = new FixedTimeProvider(RuleBuilder.At("2026-11-16T23:30:00Z"), brussels);

		CardState.Evaluate(new CardPeriod(null, new DateOnly(2026, 11, 16)), late).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a missing clock is refused.
	/// </summary>
	[Fact]
	public void Evaluate_NullClock_ShouldThrow()
	{
		var act = () => CardState.Evaluate(null, null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
