using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="NearestSpotViewModel"/>: the states of the block, the distance and walking time text, the
///              newest search winning over an older one, and the navigation buttons going through the link launcher.
/// </summary>
public sealed class NearestSpotViewModelTests
{
	#region Static Instances
	/// <summary>
	/// The spot the searches find.
	/// </summary>
	private static readonly FreeSpot Best = new(new GeoPoint(50.8252, 4.3608), 25, "Rue Test", "Ixelles", "Rue Test, Ixelles");
	#endregion

	#region State Tests
	/// <summary>
	/// Verifies the text under the best spot and the line of the other spots.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_SpotsFound_ShouldWriteDistanceWalkAndOthers()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches(Best, Best with { Metres = 140.4, Label = "Rue Autre, Ixelles" });

		var best = await ui.Nearest.ShowAsync(UiFixture.Here);

		best.Should().Be(Best);
		ui.Nearest.State.Should().Be(NearestSpotState.Found);
		ui.Nearest.BestSubtitle.Should().Be("Free with your card · 25 m · about 1 min on foot");
		ui.Nearest.OthersText.Should().Be("Also nearby: Rue Autre, Ixelles (140 m)");
	}

	/// <summary>
	/// Verifies the walking time of a longer distance.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_FarSpot_ShouldEstimateTheWalkAtEightyMetresPerMinute()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches(Best with { Metres = 400 });

		await ui.Nearest.ShowAsync(UiFixture.Here);

		ui.Nearest.BestSubtitle.Should().Be("Free with your card · 400 m · about 5 min on foot");
	}

	/// <summary>
	/// Verifies the states for no spot and for a failed search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_NoneOrFailed_ShouldSayWhy()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches();
		await ui.Nearest.ShowAsync(UiFixture.Here);
		ui.Nearest.State.Should().Be(NearestSpotState.NoneFound);

		ui.Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<FreeSpot>>(new Error("Urbis.Unavailable", "UrbIS unreachable")));
		await ui.Nearest.ShowAsync(UiFixture.Here);

		ui.Nearest.State.Should().Be(NearestSpotState.Failed);
		ui.Nearest.FailureMessage.Should().Be("Could not search right now (UrbIS unreachable).");
	}

	/// <summary>
	/// Verifies that a new search forgets the other spots of the old one and that a failure is forgotten by the next search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_NewSearch_ShouldForgetTheOldSpotsAndTheOldFailure()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches(Best, Best with { Label = "Rue Autre, Ixelles" });
		await ui.Nearest.ShowAsync(UiFixture.Here);
		ui.Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<FreeSpot>>(new Error("Urbis.Unavailable", "UrbIS unreachable")));

		await ui.Nearest.ShowAsync(UiFixture.Here);

		ui.Nearest.Others.Should().BeEmpty();
		ui.Nearest.OthersText.Should().BeEmpty();
		ui.Nearest.FailureMessage.Should().NotBeEmpty();
		ui.AnswerSearches(Best);

		await ui.Nearest.ShowAsync(UiFixture.Here);

		ui.Nearest.FailureMessage.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the answer of an older search does not replace the newer one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_OlderSearchAnswersLast_ShouldBeDropped()
	{
		using var ui = new UiFixture();
		var older = new TaskCompletionSource<Result<IReadOnlyList<FreeSpot>>>();
		var newer = new TaskCompletionSource<Result<IReadOnlyList<FreeSpot>>>();
		ui.Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(older.Task, newer.Task);

		var first = ui.Nearest.ShowAsync(UiFixture.Here);
		var second = ui.Nearest.ShowAsync(UiFixture.Here);
		newer.SetResult(Result.Success<IReadOnlyList<FreeSpot>>([Best]));
		older.SetResult(Result.Success<IReadOnlyList<FreeSpot>>([Best with { Label = "Old" }]));

		(await first).Should().BeNull();
		(await second).Should().Be(Best);
		ui.Nearest.Best!.Label.Should().Be("Rue Test, Ixelles");
	}
	#endregion

	#region Link Tests
	/// <summary>
	/// Verifies that the Waze and Google Maps buttons open the best spot through the launcher.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_BestSpot_ShouldGoThroughTheLauncher()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches(Best);
		await ui.Nearest.ShowAsync(UiFixture.Here);

		await ui.Nearest.OpenWazeAsync();
		await ui.Nearest.OpenGoogleMapsAsync();

		await ui.Launcher.Received(1).OpenAsync(new Uri("https://waze.com/ul?ll=50.825200,4.360800&navigate=yes"), Arg.Any<CancellationToken>());
		await ui.Launcher.Received(1).OpenAsync(new Uri("https://www.google.com/maps/dir/?api=1&destination=50.825200,4.360800&travelmode=driving"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that hiding the block drops the spots and the failure and that the links do nothing without a spot.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Hide_AfterFound_ShouldDropTheSpot()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches(Best, Best with { Label = "Rue Autre, Ixelles" });
		await ui.Nearest.ShowAsync(UiFixture.Here);

		ui.Nearest.Hide();
		await ui.Nearest.OpenWazeAsync();

		ui.Nearest.State.Should().Be(NearestSpotState.Hidden);
		ui.Nearest.Others.Should().BeEmpty();
		ui.Nearest.FailureMessage.Should().BeEmpty();
		await ui.Launcher.DidNotReceiveWithAnyArgs().OpenAsync(default!, default);
	}
	#endregion
}
