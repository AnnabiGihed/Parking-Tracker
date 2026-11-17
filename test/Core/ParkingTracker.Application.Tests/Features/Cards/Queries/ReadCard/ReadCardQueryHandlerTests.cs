using NSubstitute;
using ParkingTracker.Application.Features.Cards.Abstractions;
using ParkingTracker.Application.Features.Cards.Queries.ReadCard;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.Cards.Queries.ReadCard;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the scan of a card: the text of the reader is parsed into fields, a photo without fields is a
///              success with nothing set, and a failure of the reader, such as the user walking away, is returned unchanged.
/// </summary>
public sealed class ReadCardQueryHandlerTests
{
	#region Handler Tests
	/// <summary>
	/// Verifies that the recognised text becomes card fields.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_TextRead_ShouldReturnTheParsedFields()
	{
		var (handler, reader, _) = Create();
		reader.ReadTextAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("B012-345-678 1ABC234"));

		var result = await handler.Handle(new ReadCardQuery(), CancellationToken.None);

		result.Value.Number.Should().Be("B012-345-678");
		result.Value.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that a photo without text is a success without fields.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_EmptyText_ShouldSucceedWithNoField()
	{
		var (handler, reader, _) = Create();
		reader.ReadTextAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(string.Empty));

		var result = await handler.Handle(new ReadCardQuery(), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.FoundFields.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a user who walked away is returned with the code of a cancellation.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_UserCancelled_ShouldReturnTheCancelledFailure()
	{
		var (handler, reader, _) = Create();
		reader.ReadTextAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<string>(CardScanErrors.Cancelled()));

		var result = await handler.Handle(new ReadCardQuery(), CancellationToken.None);

		result.Error.Code.Should().Be(CardScanErrors.CancelledCode);
	}

	/// <summary>
	/// Verifies that reference data that cannot be read is returned as the failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ReferenceDataMissing_ShouldReturnTheFailure()
	{
		var (handler, reader, data) = Create();
		var missing = new Error("ReferenceData.Missing", "no data");
		data.GetReferenceData().Returns(Result.Failure<ReferenceDataSet>(missing));
		reader.ReadTextAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("B012-345-678"));

		var result = await handler.Handle(new ReadCardQuery(), CancellationToken.None);

		result.Error.Should().Be(missing);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the handler over substitutes.
	/// </summary>
	/// <returns>The handler, the reader and the reference data.</returns>
	private static (ReadCardQueryHandler Handler, ICardTextReader Reader, IReferenceData Data) Create()
	{
		var reader = Substitute.For<ICardTextReader>();
		var data = Substitute.For<IReferenceData>();
		data.GetReferenceData().Returns(Result.Success(new ReferenceDataSet([], [], [])));
		return (new ReadCardQueryHandler(reader, data), reader, data);
	}
	#endregion
}
