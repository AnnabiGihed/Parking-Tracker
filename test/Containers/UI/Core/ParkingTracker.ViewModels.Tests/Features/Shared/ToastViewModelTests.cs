using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ToastViewModel"/>: a message shows at once and hides after its time on the fake clock; a
///              newer message restarts the time.
/// </summary>
public sealed class ToastViewModelTests
{
	#region Show Tests
	/// <summary>
	/// Verifies that a message hides after 3.5 seconds.
	/// </summary>
	[Fact]
	public void Show_Message_ShouldHideAfterThreeAndAHalfSeconds()
	{
		using var ui = new UiFixture();
		ui.Toast.Show("Hello");

		ui.Toast.IsVisible.Should().BeTrue();
		ui.Clock.Advance(TimeSpan.FromMilliseconds(3499));
		ui.Toast.IsVisible.Should().BeTrue();
		ui.Clock.Advance(TimeSpan.FromMilliseconds(1));

		ui.Toast.IsVisible.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a newer message restarts the time of the older one.
	/// </summary>
	[Fact]
	public void Show_SecondMessage_ShouldRestartTheTime()
	{
		using var ui = new UiFixture();
		ui.Toast.Show("First");
		ui.Clock.Advance(TimeSpan.FromSeconds(3));
		ui.Toast.Show("Second", TimeSpan.FromSeconds(8));

		ui.Clock.Advance(TimeSpan.FromSeconds(7));

		ui.Toast.IsVisible.Should().BeTrue();
		ui.Toast.Message.Should().Be("Second");
	}
	#endregion
}
