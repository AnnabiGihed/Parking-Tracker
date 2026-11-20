using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="BackNavigation"/>: the back button closes what was opened last, once, and does nothing
///              when nothing is open; a screen that closed by itself is no longer asked.
/// </summary>
public sealed class BackNavigationTests
{
	#region TryGoBack Tests
	/// <summary>
	/// Verifies that nothing is closed, and the host is told so, while no screen is registered.
	/// </summary>
	[Fact]
	public void TryGoBack_NothingRegistered_ShouldReturnFalse()
	{
		var navigation = new BackNavigation();

		navigation.CanGoBack.Should().BeFalse();
		navigation.TryGoBack().Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the screen opened last is closed first and that each press closes one screen.
	/// </summary>
	[Fact]
	public void TryGoBack_TwoRegistered_ShouldCloseTheNewestFirst()
	{
		var navigation = new BackNavigation();
		var closed = new List<string>();
		navigation.Register(() => closed.Add("menu"));
		navigation.Register(() => closed.Add("dialog"));

		var first = navigation.TryGoBack();
		var second = navigation.TryGoBack();
		var third = navigation.TryGoBack();

		closed.Should().Equal("dialog", "menu");
		first.Should().BeTrue();
		second.Should().BeTrue();
		third.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a registration disposed by its screen is not asked to close.
	/// </summary>
	[Fact]
	public void TryGoBack_RegistrationDisposed_ShouldNotCloseIt()
	{
		var navigation = new BackNavigation();
		var closed = false;
		navigation.Register(() => closed = true).Dispose();

		var result = navigation.TryGoBack();

		result.Should().BeFalse();
		closed.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a close action that disposes its own registration, as a closing screen does, closes once.
	/// </summary>
	[Fact]
	public void TryGoBack_ActionDisposesItsRegistration_ShouldCloseOnce()
	{
		var navigation = new BackNavigation();
		var count = 0;
		IDisposable? registration = null;
		registration = navigation.Register(() =>
		{
			count++;
			registration!.Dispose();
		});

		navigation.TryGoBack();

		count.Should().Be(1);
		navigation.CanGoBack.Should().BeFalse();
	}
	#endregion

	#region Register Tests
	/// <summary>
	/// Verifies that a missing close action is refused.
	/// </summary>
	[Fact]
	public void Register_NullAction_ShouldThrowArgumentNullException()
	{
		var navigation = new BackNavigation();

		var act = () => navigation.Register(null!); // null on purpose: proves the guard clause rejects it

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
