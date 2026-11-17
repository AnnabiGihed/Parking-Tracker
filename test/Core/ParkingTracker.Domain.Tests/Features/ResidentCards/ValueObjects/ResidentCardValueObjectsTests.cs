using ParkingTracker.Domain.Features.ResidentCards.Errors;
using ParkingTracker.Domain.Features.ResidentCards.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.ResidentCards.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the small value objects of the residents' card: <see cref="LicencePlate"/>, <see cref="CardNumber"/>,
///              <see cref="MunicipalityCode"/> and <see cref="SectorCode"/> normalise their text and have value equality.
/// </summary>
public sealed class ResidentCardValueObjectsTests
{
	#region LicencePlate Tests
	/// <summary>
	/// Verifies that a plate is put in capitals and loses its white space and dashes.
	/// </summary>
	/// <param name="typed">The plate as typed.</param>
	[Theory]
	[InlineData("1abc234")]
	[InlineData(" 1-ABC 234 ")]
	[InlineData("1\tab-c2 34")]
	public void LicencePlate_CreateOptional_ShouldNormalise(string typed)
	{
		LicencePlate.CreateOptional(typed)!.Value.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that nothing left after normalising means no plate.
	/// </summary>
	/// <param name="typed">The plate as typed.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" - ")]
	public void LicencePlate_CreateOptional_NothingLeft_ShouldBeNull(string? typed)
	{
		LicencePlate.CreateOptional(typed).Should().BeNull();
	}

	/// <summary>
	/// Verifies that two plates with the same text are equal.
	/// </summary>
	[Fact]
	public void LicencePlate_SameText_ShouldBeEqual()
	{
		LicencePlate.CreateOptional("1abc234").Should().Be(LicencePlate.CreateOptional("1-ABC-234"));
	}
	#endregion

	#region CardNumber Tests
	/// <summary>
	/// Verifies that the number is trimmed and a blank one is absent.
	/// </summary>
	[Fact]
	public void CardNumber_CreateOptional_ShouldTrimAndTreatBlankAsAbsent()
	{
		CardNumber.CreateOptional("  B000-000-001 ")!.Value.Should().Be("B000-000-001");
		CardNumber.CreateOptional("   ").Should().BeNull();
		CardNumber.CreateOptional(null).Should().BeNull();
	}
	#endregion

	#region MunicipalityCode Tests
	/// <summary>
	/// Verifies that the code is trimmed.
	/// </summary>
	[Fact]
	public void MunicipalityCode_Create_ShouldTrim()
	{
		var result = MunicipalityCode.Create(" saint-gilles ");

		result.IsSuccess.Should().BeTrue();
		result.Value.Value.Should().Be("saint-gilles");
	}

	/// <summary>
	/// Verifies that a missing or blank code fails with the municipality error.
	/// </summary>
	/// <param name="text">The text of the code.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("  ")]
	public void MunicipalityCode_Create_Blank_ShouldFail(string? text)
	{
		MunicipalityCode.Create(text).Error.Should().Be(ResidentCardErrors.MunicipalityRequired);
	}
	#endregion

	#region SectorCode Tests
	/// <summary>
	/// Verifies that the code is trimmed and a blank one is absent.
	/// </summary>
	[Fact]
	public void SectorCode_CreateOptional_ShouldTrimAndTreatBlankAsAbsent()
	{
		SectorCode.CreateOptional(" louise ")!.Value.Should().Be("louise");
		SectorCode.CreateOptional(" ").Should().BeNull();
		SectorCode.CreateOptional(null).Should().BeNull();
	}
	#endregion
}
