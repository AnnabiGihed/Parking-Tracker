using NSubstitute;
using ParkingTracker.Application.Features.Sectors.Queries.GetMailles;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.Sectors.Queries.GetMailles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="GetMaillesQueryHandler"/>: the areas of a municipality are picked by code prefix and
///              sorted by name, the border zones by code, and an unknown municipality or an unreadable list is a failed result.
/// </summary>
public sealed class GetMaillesQueryHandlerTests
{
	#region Test Infrastructure
	/// <summary>
	/// Builds a handler over a sector list and one municipality.
	/// </summary>
	/// <param name="options">The sector list the source returns.</param>
	/// <returns>The handler.</returns>
	private static GetMaillesQueryHandler HandlerFor(Result<IReadOnlyList<MailleOption>> options)
	{
		var parking = Substitute.For<IParkingBrusselsClient>();
		parking.GetMailleOptionsAsync(Arg.Any<CancellationToken>()).Returns(options);
		var data = Substitute.For<IReferenceData>();
		var ixelles = new Municipality("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR", "ELSR"], ["LIM_X"], null);
		data.GetReferenceData().Returns(Result.Success(new ReferenceDataSet([ixelles], [], [])));
		return new GetMaillesQueryHandler(parking, data);
	}
	#endregion

	#region Selection Tests
	/// <summary>
	/// Verifies the prefix rule (the code itself or the prefix followed by an underscore), the sorting and the border zones.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_Municipality_ShouldPickAreasByPrefixSortedByName()
	{
		var handler = HandlerFor(Result.Success<IReadOnlyList<MailleOption>>(
		[
			new MailleOption("IXLR_2", "Zeta"),
			new MailleOption("IXLRX_1", "Not mine"),
			new MailleOption("ELSR_1", "Alpha"),
			new MailleOption("IXLR", null),
			new MailleOption("LIM_X", "Border"),
			new MailleOption("OTHER_1", "Other"),
		]));

		var result = await handler.Handle(new GetMaillesQuery("ixelles"), CancellationToken.None);

		result.Value.Areas.Select(area => area.ZoneCode).Should().Equal("ELSR_1", "IXLR", "IXLR_2");
		result.Value.BorderZones.Select(zone => zone.ZoneCode).Should().Equal("LIM_X");
	}

	/// <summary>
	/// Verifies that a municipality missing from the reference data is a failed result.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_UnknownMunicipality_ShouldFail()
	{
		var handler = HandlerFor(Result.Success<IReadOnlyList<MailleOption>>([]));

		var result = await handler.Handle(new GetMaillesQuery("nowhere"), CancellationToken.None);

		result.Error.Code.Should().Be(DecisionContextFactory.MunicipalityMissingCode);
	}

	/// <summary>
	/// Verifies that an unreadable sector list is returned as the failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_SourceFails_ShouldReturnTheFailure()
	{
		var down = new Error("ParkingBrussels.Unavailable", "parking.brussels HTTP 500");
		var handler = HandlerFor(Result.Failure<IReadOnlyList<MailleOption>>(down));

		var result = await handler.Handle(new GetMaillesQuery("ixelles"), CancellationToken.None);

		result.Error.Should().Be(down);
	}
	#endregion

	#region Validation Tests
	/// <summary>
	/// Verifies that a query without a municipality is rejected.
	/// </summary>
	[Fact]
	public void Validate_EmptyMunicipality_ShouldBeRejected()
	{
		new GetMaillesQueryValidator().Validate(new GetMaillesQuery(string.Empty)).IsValid.Should().BeFalse();
		new GetMaillesQueryValidator().Validate(new GetMaillesQuery("ixelles")).IsValid.Should().BeTrue();
	}
	#endregion
}
