using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Stubs shared by the tests of the area queries: a context factory and an area status provider that return fixed results.
/// </summary>
internal static class AreaQueryStubs
{
	#region Fields
	/// <summary>
	/// The error a failing source gives.
	/// </summary>
	public static readonly Error Down = new("Test.Down", "source down");

	/// <summary>
	/// The municipality the tests use.
	/// </summary>
	public static readonly Municipality Etterbeek = new("etterbeek", "Etterbeek", "Etterbeek", "ETTERBEEK", MunicipalityKind.Whole, [], [], null);
	#endregion

	#region Public Methods
	/// <summary>
	/// Creates a context factory that always returns the given result.
	/// </summary>
	/// <param name="result">Result the stub returns.</param>
	/// <returns>Stub context factory.</returns>
	public static IDecisionContextFactory Contexts(Result<DecisionContext> result)
	{
		var contexts = Substitute.For<IDecisionContextFactory>();
		contexts.CreateAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(result);
		return contexts;
	}

	/// <summary>
	/// Creates a successful context for a card of the whole municipality.
	/// </summary>
	/// <returns>Successful decision context.</returns>
	public static Result<DecisionContext> ValidContext()
	{
		return Result.Success(new DecisionContext(new ParkingCard("etterbeek", null, new CardPeriod(null, null), [], [], []), Etterbeek, null, null));
	}

	/// <summary>
	/// Creates an area status provider that always returns the given result.
	/// </summary>
	/// <param name="result">Result the stub returns.</param>
	/// <returns>Stub area status provider.</returns>
	public static IAreaStatusProvider Areas(Result<IReadOnlyList<SurfaceStatus>> result)
	{
		var areas = Substitute.For<IAreaStatusProvider>();
		areas.LoadAsync(Arg.Any<DecisionContext>(), Arg.Any<GeoBox>(), Arg.Any<DateTimeOffset>(), Arg.Any<AddressFetching>(), Arg.Any<CancellationToken>()).Returns(result);
		return areas;
	}

	/// <summary>
	/// Creates the failure the area status provider reports when a source is down.
	/// </summary>
	/// <returns>Failed result.</returns>
	public static Result<IReadOnlyList<SurfaceStatus>> Failed() => Result.Failure<IReadOnlyList<SurfaceStatus>>(Down);
	#endregion
}
