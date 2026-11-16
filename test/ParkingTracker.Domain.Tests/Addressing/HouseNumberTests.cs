using ParkingTracker.Domain.Addressing;

namespace ParkingTracker.Domain.Tests.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="HouseNumber"/> and <see cref="HouseRange"/>: parsing and the ordering keys of live.js.
/// </summary>
public sealed class HouseNumberTests
{
	#region Parse Tests
	/// <summary>
	/// Verifies that a number with or without a letter is read, the letter in lower case.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <param name="number">The expected number.</param>
	/// <param name="letter">The expected letter.</param>
	[Theory]
	[InlineData("57b", 57, "b")]
	[InlineData("74A", 74, "a")]
	[InlineData("12", 12, "")]
	[InlineData(" 8 ", 8, "")]
	[InlineData("7 c", 7, "c")]
	[InlineData("17-57b", 17, "")]
	[InlineData("0", 0, "")]
	public void Parse_Text_ShouldReadNumberAndLetter(string text, int number, string letter)
	{
		var house = HouseNumber.Parse(text);

		house.Should().Be(new HouseNumber(number, letter));
	}

	/// <summary>
	/// Verifies that text without a leading number, empty text and a number too large for an integer give no house number.
	/// </summary>
	/// <param name="text">The text.</param>
	[Theory]
	[InlineData("abc")]
	[InlineData("")]
	[InlineData(null)]
	[InlineData("99999999999")]
	public void Parse_NoNumber_ShouldReturnNull(string? text)
	{
		HouseNumber.Parse(text).Should().BeNull();
	}
	#endregion

	#region Key Tests
	/// <summary>
	/// Verifies the ordering keys: a number without a letter spans all its letters as the end of a range.
	/// </summary>
	[Fact]
	public void Keys_NumberWithoutLetter_ShouldCoverAllItsLetters()
	{
		var house = new HouseNumber(74, string.Empty);

		house.LowKey.Should().Be(7400);
		house.HighKey.Should().Be(7499);
	}

	/// <summary>
	/// Verifies that a letter ranks a = 1, b = 2 for both keys.
	/// </summary>
	[Fact]
	public void Keys_NumberWithLetter_ShouldRankTheLetter()
	{
		new HouseNumber(74, "a").LowKey.Should().Be(7401);
		new HouseNumber(74, "b").HighKey.Should().Be(7402);
	}

	/// <summary>
	/// Verifies the odd and even side of the street.
	/// </summary>
	[Fact]
	public void IsOdd_OddAndEvenNumbers_ShouldTellTheSide()
	{
		new HouseNumber(57, "b").IsOdd.Should().BeTrue();
		new HouseNumber(58, string.Empty).IsOdd.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the side follows the parity of the number.
	/// </summary>
	[Fact]
	public void Side_OddAndEvenNumbers_ShouldBeOddOrEven()
	{
		new HouseNumber(57, "b").Side.Should().Be(StreetSide.Odd);
		new HouseNumber(58, string.Empty).Side.Should().Be(StreetSide.Even);
	}
	#endregion

	#region Range Tests
	/// <summary>
	/// Verifies that the range contains its ends and nothing beyond.
	/// </summary>
	[Fact]
	public void Contains_NumbersAroundTheEnds_ShouldIncludeOnlyTheRange()
	{
		var range = new HouseRange(1700, 5702);

		range.Contains(new HouseNumber(17, string.Empty)).Should().BeTrue();
		range.Contains(new HouseNumber(57, "b")).Should().BeTrue();
		range.Contains(new HouseNumber(57, "c")).Should().BeFalse();
		range.Contains(new HouseNumber(16, string.Empty)).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a missing house number is refused.
	/// </summary>
	[Fact]
	public void Contains_Null_ShouldThrow()
	{
		var act = () => new HouseRange(0, 1).Contains(null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
