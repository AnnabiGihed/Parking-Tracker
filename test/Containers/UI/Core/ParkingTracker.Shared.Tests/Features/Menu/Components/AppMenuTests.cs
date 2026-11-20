using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Shared.Features.SpotCheck.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;
using Radzen.Blazor;

namespace ParkingTracker.Shared.Tests.Features.Menu.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the side menu (ADR-0030): the menu button of the search bar opens it as a Radzen side dialog, it
///              lists Home, Work and the parking card with what is saved under each, a tap closes it and opens the screen of the
///              entry, and the back button of the phone closes it.
/// </summary>
public sealed class AppMenuTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// A saved home.
	/// </summary>
	private static readonly PlaceShortcut SavedHome = new(PlaceKind.Home, "Home", Here, "Rue de la Loi 1");

	/// <summary>
	/// Renders the search bar and the Radzen host that draws the dialogs, and taps the menu button.
	/// </summary>
	/// <returns>The Radzen dialog host, which holds the menu while it is open.</returns>
	private async Task<IRenderedComponent<RadzenDialog>> OpenMenuAsync()
	{
		var host = Render<RadzenDialog>();
		var bar = Render<SearchBar>();

		await bar.Find("#menuBtn").ClickAsync().ConfigureAwait(false);

		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().HaveCount(1)).ConfigureAwait(false);
		return host;
	}
	#endregion

	#region Open Tests
	/// <summary>
	/// Verifies that the menu button is a Radzen button with the id the end-to-end tests use, and that a tap opens the menu on the
	/// left, over a mask, in a Radzen side dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MenuButton_Tap_ShouldOpenTheMenuInASideDialog()
	{
		var bar = Render<SearchBar>();
		bar.Find("#menuBtn").ClassList.Should().Contain("rz-button");
		bar.FindAll(".searchbar-logo").Should().BeEmpty();

		var host = await OpenMenuAsync();

		host.FindAll(".rz-dialog-side-position-left").Should().HaveCount(1);
		host.FindAll(".rz-dialog-side.app-menu-dialog").Should().HaveCount(1);
		host.FindAll(".rz-dialog-mask").Should().NotBeEmpty();
		Resolve<IBackNavigation>().CanGoBack.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the menu lists Home, Work and the parking card, each with "Not set" while nothing is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_NothingSaved_ShouldListThreeItemsWithNotSet()
	{
		var host = await OpenMenuAsync();

		host.Find("#appMenu .app-menu-name").TextContent.Should().Be("Parking Tracker");
		host.FindAll("#appMenu .app-menu-item").Should().HaveCount(3);
		host.FindAll("#appMenu .app-menu-title").Select(title => title.TextContent).Should().Equal("Home", "Work", "Parking card");
		host.FindAll("#appMenu .app-menu-sub").Select(text => text.TextContent).Should().AllBe("Not set");
	}

	/// <summary>
	/// Verifies that a saved home shows its address under Home.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_HomeSaved_ShouldShowTheAddressUnderHome()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([SavedHome]);
		await Resolve<PlacesViewModel>().LoadAsync();

		var host = await OpenMenuAsync();

		host.FindAll("#appMenu .app-menu-sub").Select(text => text.TextContent).Should().Equal("Rue de la Loi 1", "Not set", "Not set");
	}
	#endregion

	#region Tap Tests
	/// <summary>
	/// Verifies that a tap on Home closes the menu and opens the place dialog for Home.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Home_Tap_ShouldCloseTheMenuAndOpenThePlaceDialogForHome()
	{
		var host = await OpenMenuAsync();

		// Radzen puts the Click callback on the list item, not on the link inside it; a browser bubbles a tap on the link up to the item.
		await host.Find("#menuHome").ClickAsync();

		var places = Resolve<PlacesViewModel>();
		await host.WaitForAssertionAsync(() => places.IsOpen.Should().BeTrue());
		places.EditingKind.Should().Be(PlaceKind.Home);
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
		Resolve<IBackNavigation>().CanGoBack.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a tap on Work opens the place dialog for Work.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Work_Tap_ShouldOpenThePlaceDialogForWork()
	{
		var host = await OpenMenuAsync();

		await host.Find("#menuWork").ClickAsync();

		var places = Resolve<PlacesViewModel>();
		await host.WaitForAssertionAsync(() => places.IsOpen.Should().BeTrue());
		places.EditingKind.Should().Be(PlaceKind.Work);
	}

	/// <summary>
	/// Verifies that a tap on the parking card closes the menu and opens the card dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_Tap_ShouldCloseTheMenuAndOpenTheCardDialog()
	{
		var host = await OpenMenuAsync();

		await host.Find("#menuCard").ClickAsync();

		await host.WaitForAssertionAsync(() => Resolve<CardViewModel>().IsOpen.Should().BeTrue());
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
	}
	#endregion

	#region Back Button Tests
	/// <summary>
	/// Verifies that the back button closes the menu and that a second press has nothing left to close.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BackButton_MenuOpen_ShouldCloseTheMenu()
	{
		var host = await OpenMenuAsync();
		var back = Resolve<IBackNavigation>();

		var closed = await host.InvokeAsync(back.TryGoBack);

		closed.Should().BeTrue();
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
		back.CanGoBack.Should().BeFalse();
		back.TryGoBack().Should().BeFalse();
	}
	#endregion
}
