using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Shared.Features.SpotCheck.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Vehicles;
using Radzen.Blazor;

namespace ParkingTracker.Shared.Tests.Features.Menu.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the side menu (ADR-0030): the menu button of the search bar opens it as a Radzen side dialog, it
///              lists Home, Work and the parking card with what is saved under each, a tap closes it and starts the picker or opens the screen of the
///              entry, and the back button of the phone closes it.
/// </summary>
public sealed class AppMenuTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// A saved home.
	/// </summary>
	private static readonly PlaceShortcut SavedHome = new(SavedPlaceId.New(), PlaceKind.Home, "Home", Here, "Rue de la Loi 1");

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
	/// Verifies that the menu lists Home, Work, the favourite places, My vehicle and the parking card, with "Not set" or "None yet" while nothing is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_NothingSaved_ShouldListTheFiveItems()
	{
		var host = await OpenMenuAsync();

		host.Find("#appMenu .app-menu-name").TextContent.Should().Be("Parking Tracker");
		host.FindAll("#appMenu .app-menu-item").Should().HaveCount(5);
		host.FindAll("#appMenu .app-menu-title").Select(title => title.TextContent).Should().Equal("Home", "Work", "Favourite places", "My vehicle", "Parking card");
		host.FindAll("#appMenu .app-menu-sub").Select(text => text.TextContent).Should().Equal("Not set", "Not set", "None yet", "Not set", "Not set");
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

		host.FindAll("#appMenu .app-menu-sub").Select(text => text.TextContent).Should().Equal("Rue de la Loi 1", "Not set", "None yet", "Not set", "Not set");
	}
	#endregion

	#region Account Entry Tests
	/// <summary>
	/// Verifies that a build without Google sign-in (one built without a Web client ID) shows no account entry.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_GoogleSignInNotAvailable_ShouldShowNoAccountEntry()
	{
		var host = await OpenMenuAsync();

		host.FindAll("#menuAccount").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a build with Google sign-in shows the account entry last, and that a tap closes the menu and opens the account sheet.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_GoogleSignInAvailable_ShouldShowTheAccountEntryAndOpenTheSheet()
	{
		Google.IsAvailable.Returns(true);
		var host = await OpenMenuAsync();

		host.FindAll("#appMenu .app-menu-title").Select(title => title.TextContent).Should().EndWith("Account");
		await host.Find("#menuAccount").ClickAsync();

		await host.WaitForAssertionAsync(() => Resolve<AccountViewModel>().IsOpen.Should().BeTrue());
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
	}
	#endregion

	#region Tap Tests
	/// <summary>
	/// Verifies that a tap on Home closes the menu and starts the map picker for Home.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Home_Tap_ShouldCloseTheMenuAndStartThePickerForHome()
	{
		var host = await OpenMenuAsync();

		// Radzen puts the Click callback on the list item, not on the link inside it; a browser bubbles a tap on the link up to the item.
		await host.Find("#menuHome").ClickAsync();

		var picker = Resolve<PlacePickerViewModel>();
		await host.WaitForAssertionAsync(() => picker.IsActive.Should().BeTrue());
		picker.Kind.Should().Be(PlaceKind.Home);
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
		Resolve<IBackNavigation>().CanGoBack.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a tap on Work starts the map picker for Work.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Work_Tap_ShouldStartThePickerForWork()
	{
		var host = await OpenMenuAsync();

		await host.Find("#menuWork").ClickAsync();

		var picker = Resolve<PlacePickerViewModel>();
		await host.WaitForAssertionAsync(() => picker.IsActive.Should().BeTrue());
		picker.Kind.Should().Be(PlaceKind.Work);
	}

	/// <summary>
	/// Verifies that the favourite places entry shows how many are saved, and that a tap closes the menu and opens the list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Favourites_Tap_ShouldCloseTheMenuAndOpenTheList()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([new PlaceShortcut(SavedPlaceId.New(), PlaceKind.Favourite, "Bakery", Here, "Rue 1")]);
		await Resolve<PlacesViewModel>().LoadAsync();
		var host = await OpenMenuAsync();
		host.Find("#menuFavourites .app-menu-sub").TextContent.Should().Be("1 saved");

		await host.Find("#menuFavourites").ClickAsync();

		await host.WaitForAssertionAsync(() => Resolve<FavouritesViewModel>().IsOpen.Should().BeTrue());
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
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

	#region Vehicle Tests
	/// <summary>
	/// Verifies that a saved vehicle shows "Make Model · Year" under My vehicle, and that a tap closes the menu and opens the vehicle dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Vehicle_Saved_ShouldShowTheSummaryAndOpenTheDialogOnATap()
	{
		Vehicles.GetAsync(Arg.Any<CancellationToken>()).Returns(new VehicleDetails("tesla", "Model 3", 2021, FuelType.Electric, EuroNorm.Unknown));
		await Resolve<VehicleViewModel>().LoadAsync();
		var host = await OpenMenuAsync();
		host.Find("#menuVehicle .app-menu-sub").TextContent.Should().Be("Tesla Model 3 · 2021");

		await host.Find("#menuVehicle").ClickAsync();

		await host.WaitForAssertionAsync(() => Resolve<VehicleViewModel>().IsOpen.Should().BeTrue());
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
