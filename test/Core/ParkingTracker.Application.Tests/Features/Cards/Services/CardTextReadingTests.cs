using System.Text.RegularExpressions;
using NSubstitute;
using ParkingTracker.Application.Features.Cards.Abstractions;
using ParkingTracker.Application.Features.Cards.Services;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.Cards.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CardTextReading"/>: the fields are returned, reference data that cannot be read is passed on
///              as it is, and a pattern that times out becomes <see cref="CardScanErrors.FailedCode"/>.
/// </summary>
public sealed class CardTextReadingTests
{
	#region Read Tests
	/// <summary>
	/// Verifies that the fields of the text are returned.
	/// </summary>
	[Fact]
	public void Read_Text_ShouldReturnTheParsedFields()
	{
		var result = CardTextReading.Read(DataWith(Result.Success(new ReferenceDataSet([], [], []))), "8012-345-678");

		result.Value.Number.Should().Be("B012-345-678");
	}

	/// <summary>
	/// Verifies that reference data that cannot be read is returned as the failure.
	/// </summary>
	[Fact]
	public void Read_ReferenceDataMissing_ShouldReturnItsError()
	{
		var missing = new Error("ReferenceData.Missing", "no data");

		var result = CardTextReading.Read(DataWith(Result.Failure<ReferenceDataSet>(missing)), "x");

		result.Error.Should().Be(missing);
	}

	/// <summary>
	/// Verifies that a pattern that times out is the failure <see cref="CardScanErrors.FailedCode"/>.
	/// </summary>
	[Fact]
	public void Read_PatternTimesOut_ShouldFailWithScanFailed()
	{
		var data = DataWith(Result.Success(new ReferenceDataSet([], [], [])));

		var result = CardTextReading.Read(data, "x", (_, _) => throw new RegexMatchTimeoutException());

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(CardScanErrors.FailedCode);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds reference data that answers with the given result.
	/// </summary>
	/// <param name="answer">What the reference data returns.</param>
	/// <returns>The reference data.</returns>
	private static IReferenceData DataWith(Result<ReferenceDataSet> answer)
	{
		var data = Substitute.For<IReferenceData>();
		data.GetReferenceData().Returns(answer);
		return data;
	}
	#endregion
}
