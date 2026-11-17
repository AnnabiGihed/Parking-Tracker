using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SpotDecision"/>: the headline and the colour of every verdict.
/// </summary>
public sealed class SpotDecisionTests
{
	#region Title and Status Tests
	/// <summary>
	/// Verifies the headline and the colour of every verdict, as the first version of the app showed them.
	/// </summary>
	/// <param name="verdict">Verdict to test.</param>
	/// <param name="title">The expected headline.</param>
	/// <param name="status">The expected colour.</param>
	[Theory]
	[InlineData(Verdict.Free, "FREE ✓", SpotStatus.Free)]
	[InlineData(Verdict.FreeNow, "FREE NOW", SpotStatus.Free)]
	[InlineData(Verdict.Pay, "PAY", SpotStatus.Pay)]
	[InlineData(Verdict.Reserved, "RESERVED", SpotStatus.Pay)]
	[InlineData(Verdict.TimeLimited, "TIME LIMITED", SpotStatus.Unknown)]
	[InlineData(Verdict.ParkingDisc, "PARKING DISC", SpotStatus.Unknown)]
	[InlineData(Verdict.CheckNumber, "CHECK THE NUMBER", SpotStatus.Unknown)]
	[InlineData(Verdict.CheckCard, "CHECK YOUR CARD", SpotStatus.Unknown)]
	[InlineData(Verdict.CheckSigns, "CHECK THE SIGNS", SpotStatus.Unknown)]
	[InlineData(Verdict.CantVerify, "CAN'T VERIFY", SpotStatus.Unknown)]
	public void TitleAndStatus_ByVerdict_ShouldMatchTheFirstVersion(Verdict verdict, string title, SpotStatus status)
	{
		var decision = SpotDecision.Create(verdict, "detail");

		decision.Title.Should().Be(title);
		decision.Status.Should().Be(status);
	}
	#endregion

	#region Headline Tests
	/// <summary>
	/// Verifies the friendly headline of every verdict; a verdict without one keeps its title.
	/// </summary>
	/// <param name="verdict">Verdict to test.</param>
	/// <param name="headline">The expected headline.</param>
	[Theory]
	[InlineData(Verdict.Free, "Free for you here")]
	[InlineData(Verdict.FreeNow, "Free for everyone right now")]
	[InlineData(Verdict.Pay, "Paid parking")]
	[InlineData(Verdict.Reserved, "Reserved space")]
	[InlineData(Verdict.TimeLimited, "Time-limited parking")]
	[InlineData(Verdict.ParkingDisc, "Blue zone: use your disc")]
	[InlineData(Verdict.CheckNumber, "Check the house number")]
	[InlineData(Verdict.CheckCard, "CHECK YOUR CARD")]
	[InlineData(Verdict.CheckSigns, "CHECK THE SIGNS")]
	[InlineData(Verdict.CantVerify, "Can't verify right now")]
	public void Headline_ByVerdict_ShouldMatchTheFirstVersion(Verdict verdict, string headline)
	{
		SpotDecision.Create(verdict, "detail").Headline.Should().Be(headline);
	}
	#endregion
}
