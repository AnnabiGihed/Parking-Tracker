using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SpotChecker"/>: the busy chip of a start that has no position yet never replaces the chips of
///              a check that a fix already started.
/// </summary>
public sealed class SpotCheckerTests
{
	#region Busy Tests
	/// <summary>
	/// Verifies that the "finding your position" chip of a start that read no position yet does not overwrite the chips of a check
	/// that a fix started meanwhile.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ShowBusy_CheckStartedMeanwhile_ShouldKeepTheChipsOfThatCheck()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Free, "Free here."));
		using var checker = new SpotChecker(ui.Sender, ui.Clock, ui.Toast, ui.Nearest, ui.Card, () => { });
		checker.ForgetHistory();
		await checker.RunAsync(UiFixture.Here, CheckSource.Me, 5);

		checker.ShowBusy("Finding your position…");

		checker.Chips.Select(chip => chip.Text).Should().Contain("My position ±5 m").And.NotContain("Finding your position…");
	}
	#endregion
}
