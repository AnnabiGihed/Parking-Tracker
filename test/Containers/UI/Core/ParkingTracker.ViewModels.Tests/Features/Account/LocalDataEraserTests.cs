using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingTracker.ViewModels.Features.Account.LocalDataEraser"/>: a full wipe cancels the reminder
///              alarms only after it committed, a failed wipe cancels and reloads nothing, and an alarm that cannot be cancelled does not
///              stop the notice and the reloads, nor change the result of the wipe.
/// </summary>
public sealed class LocalDataEraserTests : IDisposable
{
	#region Test Infrastructure
	/// <summary>
	/// The fixture that holds the eraser and its substitutes.
	/// </summary>
	private readonly UiFixture ui = new();

	/// <inheritdoc />
	public void Dispose() => ui.Dispose();
	#endregion

	#region Wipe Tests
	/// <summary>
	/// Verifies that a full wipe cancels the alarms after the commit and reloads the screens.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeAllAsync_ShouldCancelTheAlarmsAndReload()
	{
		var noticed = 0;
		ui.ResetNotice.StaleRowsRemoved += (_, _) => noticed++;
		ui.PlacesStore.ClearReceivedCalls();

		var result = await ui.Eraser.WipeAllAsync();

		result.IsSuccess.Should().BeTrue();
		await ui.Alarms.Received(1).CancelAsync(Arg.Any<CancellationToken>());
		noticed.Should().Be(1);
		await ui.PlacesStore.Received(1).GetPlacesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a failed wipe cancels no alarm and reloads nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeAllAsync_Fails_ShouldCancelNothingAndReloadNothing()
	{
		ui.Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")));
		var noticed = 0;
		ui.ResetNotice.StaleRowsRemoved += (_, _) => noticed++;

		var result = await ui.Eraser.WipeAllAsync();

		result.IsFailure.Should().BeTrue();
		await ui.Alarms.DidNotReceiveWithAnyArgs().CancelAsync();
		noticed.Should().Be(0);
	}

	/// <summary>
	/// Verifies that an alarm that cannot be cancelled still lets the notice and the reloads run, and the wipe reports success.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeAllAsync_AlarmCancelFails_ShouldStillReloadAndSucceed()
	{
		ui.Alarms.CancelAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("alarm"));
		var noticed = 0;
		ui.ResetNotice.StaleRowsRemoved += (_, _) => noticed++;
		ui.PlacesStore.ClearReceivedCalls();

		var result = await ui.Eraser.WipeAllAsync();

		result.IsSuccess.Should().BeTrue();
		noticed.Should().Be(1);
		await ui.PlacesStore.Received(1).GetPlacesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the profile wipe leaves the alarms alone, because the parking session stays.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeProfileAsync_ShouldNotCancelTheAlarms()
	{
		var result = await ui.Eraser.WipeProfileAsync();

		result.IsSuccess.Should().BeTrue();
		await ui.Alarms.DidNotReceiveWithAnyArgs().CancelAsync();
	}
	#endregion
}
