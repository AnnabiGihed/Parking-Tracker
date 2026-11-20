using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.ViewModels.Tests.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CardValidity"/>: the proposed last day is one year after the first day, and 29 February
///              gives 28 February of the next year.
/// </summary>
public sealed class CardValidityTests
{
	#region SuggestValidTo Tests
	/// <summary>
	/// Verifies that a plain date gives the same day one year later.
	/// </summary>
	[Fact]
	public void SuggestValidTo_PlainDate_ShouldAddOneYear()
	{
		CardValidity.SuggestValidTo(new DateOnly(2026, 3, 15)).Should().Be(new DateOnly(2027, 3, 15));
	}

	/// <summary>
	/// Verifies that 29 February gives 28 February of the next year.
	/// </summary>
	[Fact]
	public void SuggestValidTo_LeapDay_ShouldGiveTheLastDayOfFebruary()
	{
		CardValidity.SuggestValidTo(new DateOnly(2028, 2, 29)).Should().Be(new DateOnly(2029, 2, 28));
	}
	#endregion
}
