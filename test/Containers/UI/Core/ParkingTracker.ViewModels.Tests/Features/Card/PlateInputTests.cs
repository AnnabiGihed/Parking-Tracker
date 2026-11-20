using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.ViewModels.Tests.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="PlateInput"/>: the typed plate is written in capitals and a missing text becomes empty.
/// </summary>
public sealed class PlateInputTests
{
	#region Normalise Tests
	/// <summary>
	/// Verifies that a missing text becomes an empty one.
	/// </summary>
	[Fact]
	public void Normalise_Null_ShouldBeEmpty()
	{
		PlateInput.Normalise(null).Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that mixed case is written in capitals.
	/// </summary>
	[Fact]
	public void Normalise_MixedCase_ShouldBeUpperCase()
	{
		PlateInput.Normalise("1aBc-234").Should().Be("1ABC-234");
	}
	#endregion
}
