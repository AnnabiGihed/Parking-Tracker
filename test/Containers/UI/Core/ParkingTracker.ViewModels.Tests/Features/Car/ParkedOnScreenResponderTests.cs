using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Car;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkedOnScreenResponder"/>: a parking found while the app is on screen shows the message with
///              the headline and checks the place, unless the user's position is already followed.
/// </summary>
public sealed class ParkedOnScreenResponderTests
{
	#region Respond Tests
	/// <summary>
	/// Verifies that the message is shown and the place is checked with the accuracy of the stop.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Respond_NotFollowing_ShouldShowTheMessageAndCheckThePlace()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "Paid."));
		var responder = new ParkedOnScreenResponder(ui.Toast, ui.Car, ui.Spot);

		await responder.RespondAsync(new ParkedOnScreenEventArgs("Paid parking", UiFixture.Here, 6));

		ui.Toast.Message.Should().Be("🚗 Parked: Paid parking");
		ui.Spot.Status.Title.Should().Be("Paid parking");
		await ui.Sender.Received(1).Send(Arg.Is<DecideSpotQuery>(query => query.AccuracyMeters.Equals(6.0)), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the message is still shown but nothing is checked while the user is followed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Respond_Following_ShouldOnlyShowTheMessage()
	{
		using var ui = new UiFixture();
		await ui.Spot.StartTrackingAsync();
		var responder = new ParkedOnScreenResponder(ui.Toast, ui.Car, ui.Spot);

		await responder.RespondAsync(new ParkedOnScreenEventArgs("Free for you here", UiFixture.Here, null));

		ui.Toast.Message.Should().Be("🚗 Parked: Free for you here");
		await ui.Sender.DidNotReceive().Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a missing event is rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Respond_NullEvent_ShouldThrow()
	{
		using var ui = new UiFixture();
		var responder = new ParkedOnScreenResponder(ui.Toast, ui.Car, ui.Spot);

		var act = () => responder.RespondAsync(null!);

		await act.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion
}
