using NSubstitute;
using ParkingTracker.Application.Features.Cards.Queries.ParseCardText;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.Cards.Queries.ParseCardText;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the parsing of an already recognised card text: the fields are returned, reference data that cannot
///              be read is a failure, and text that is missing or too long is rejected.
/// </summary>
public sealed class ParseCardTextQueryHandlerTests
{
	#region Handler Tests
	/// <summary>
	/// Verifies that the fields of the text are returned.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_Text_ShouldReturnTheParsedFields()
	{
		var data = Substitute.For<IReferenceData>();
		data.GetReferenceData().Returns(Result.Success(new ReferenceDataSet([], [], [])));

		var result = await new ParseCardTextQueryHandler(data).Handle(new ParseCardTextQuery("8012-345-678"), CancellationToken.None);

		result.Value.Number.Should().Be("B012-345-678");
	}

	/// <summary>
	/// Verifies that reference data that cannot be read is a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ReferenceDataMissing_ShouldFail()
	{
		var data = Substitute.For<IReferenceData>();
		var missing = new Error("ReferenceData.Missing", "no data");
		data.GetReferenceData().Returns(Result.Failure<ReferenceDataSet>(missing));

		var result = await new ParseCardTextQueryHandler(data).Handle(new ParseCardTextQuery("x"), CancellationToken.None);

		result.Error.Should().Be(missing);
	}
	#endregion

	#region Validation Tests
	/// <summary>
	/// Verifies which texts are accepted: empty is, missing is not.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <param name="expected">Whether the query is valid.</param>
	[Theory]
	[InlineData("B012-345-678", true)]
	[InlineData("", true)]
	[InlineData(null, false)]
	public void Validate_Text_ShouldRejectOnlyMissingText(string? text, bool expected)
	{
		new ParseCardTextQueryValidator().Validate(new ParseCardTextQuery(text!)).IsValid.Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a text longer than the limit is rejected.
	/// </summary>
	[Fact]
	public void Validate_TooLong_ShouldBeRejected()
	{
		var text = new string('a', ParseCardTextQueryValidator.MaxLength + 1);

		new ParseCardTextQueryValidator().Validate(new ParseCardTextQuery(text)).IsValid.Should().BeFalse();
	}
	#endregion
}
