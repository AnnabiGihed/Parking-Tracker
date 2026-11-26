using Bunit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Contracts.Features.Accounts;
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
using Pivot.Framework.Domain.Shared;
using Radzen.Blazor;

namespace ParkingTracker.Shared.Tests.Features.Menu.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the side menu (ADR-0030): the menu button of the search bar opens it as a Radzen side dialog, it
///              lists Home, Work and the parking card with what is saved under each, a tap closes it and starts the picker or opens the screen of the
///              entry, and the back button of the phone closes it. In a build that can sign in it is one Account section: the account line, the label, Sign out and Delete account.
/// </summary>
public sealed class AppMenuTests : UiTestContext
{
	#region Static Instances
	/// <summary>
	/// The account the API returns.
	/// </summary>
	private static readonly MeResponse Me = new(Guid.Parse("0f3c2a1b-8d4e-4f60-9a7b-123456789abc"), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17), 2);
	#endregion

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

	/// <summary>
	/// Makes the build able to sign in, sets whether the phone holds a session, and opens the menu.
	/// </summary>
	/// <param name="signedIn">Whether the phone holds a session.</param>
	/// <returns>The Radzen dialog host, which holds the menu while it is open.</returns>
	private async Task<IRenderedComponent<RadzenDialog>> OpenAccountMenuAsync(bool signedIn)
	{
		Google.IsAvailable.Returns(true);
		Session.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(signedIn);
		Session.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		Session.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		return await OpenMenuAsync().ConfigureAwait(false);
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

	#region Account Section Tests
	/// <summary>
	/// Verifies that a build without Google sign-in shows no account line, no Sign out or Delete account, and the label "On this phone only".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_GoogleSignInNotAvailable_ShouldShowNoAccountEntry()
	{
		var host = await OpenMenuAsync();

		host.FindAll("#menuAccount, #menuSignOut, #menuDeleteAccount").Should().BeEmpty();
		host.Find("#menuSectionLabel").TextContent.Should().Be("On this phone only");
		await Session.DidNotReceive().IsSignedInAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that signed out, the account line offers the sign-in, the label says "On this phone only", and a tap opens the account sheet.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_SignedOut_ShouldOfferSignInAndOpenTheSheet()
	{
		var host = await OpenAccountMenuAsync(signedIn: false);

		host.Find("#menuAccount .app-menu-title").TextContent.Should().Be("Sign in");
		host.Find("#menuAccountText").TextContent.Should().Be("Keep your data with your Google account");
		host.Find("#menuSectionLabel").TextContent.Should().Be("On this phone only");
		host.FindAll("#appMenu .app-menu-item").Should().HaveCount(6);
		host.FindAll("#menuSignOut, #menuDeleteAccount").Should().BeEmpty();
		await host.Find("#menuAccount").ClickAsync();

		await host.WaitForAssertionAsync(() => Resolve<AccountViewModel>().IsOpen.Should().BeTrue());
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
	}

	/// <summary>
	/// Verifies that signed in, the account line shows the short id, the label says "Saved with your account", and Sign out and Delete account close the section.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_SignedIn_ShouldShowTheAccountLineTheLabelAndTheActions()
	{
		var host = await OpenAccountMenuAsync(signedIn: true);

		await host.WaitForAssertionAsync(() => host.Find("#menuAccountText").TextContent.Should().Be("Signed in · 0f3c2a1b"));
		host.Find("#menuAccount .app-menu-title").TextContent.Should().Be("Account");
		host.Find("#menuSectionLabel").TextContent.Should().Be("Saved with your account");
		host.FindAll("#appMenu .app-menu-title").Select(title => title.TextContent).Should().Equal("Account", "Home", "Work", "Favourite places", "My vehicle", "Parking card", "Sign out", "Delete account");
	}

	/// <summary>
	/// Verifies that a menu opened signed in shows "Signed in" alone while the account is not known.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Menu_SignedInAccountUnknown_ShouldShowSignedInAlone()
	{
		Google.IsAvailable.Returns(true);
		Session.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);
		Session.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<MeResponse>(AccountErrors.Network()));

		var host = await OpenMenuAsync();

		host.Find("#menuAccountText").TextContent.Should().Be("Signed in");
		Resolve<AccountViewModel>().Error.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that Sign out calls the client and the menu switches to the signed-out state, without closing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOut_Tap_ShouldSignOutAndShowTheSignedOutMenu()
	{
		var host = await OpenAccountMenuAsync(signedIn: true);

		await host.Find("#menuSignOut").ClickAsync();

		await host.WaitForAssertionAsync(() => host.Find("#menuAccount .app-menu-title").TextContent.Should().Be("Sign in"));
		await Session.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
		host.Find("#menuSectionLabel").TextContent.Should().Be("On this phone only");
		host.FindAll("#menuSignOut, #menuDeleteAccount").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a sign-out that fails shows the reason in the menu and keeps the menu signed in, whether the client refuses or the secure storage fails.
	/// </summary>
	/// <param name="storageFails"><see langword="true"/> when the secure storage throws, <see langword="false"/> when the client returns a failure.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SignOut_Fails_ShouldShowTheReasonAndStaySignedIn(bool storageFails)
	{
		var host = await OpenAccountMenuAsync(signedIn: true);
		if (storageFails)
		{
			Session.SignOutAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new IOException());
		}
		else
		{
			Session.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(AccountErrors.Network()));
		}

		await host.Find("#menuSignOut").ClickAsync();

		await host.WaitForAssertionAsync(() => host.Find("#menuAccountError").TextContent.Should().NotBeNullOrEmpty());
		host.Find("#menuAccount .app-menu-title").TextContent.Should().Be("Account");
		host.FindAll("#menuSignOut").Should().HaveCount(1);
	}

	/// <summary>
	/// Verifies that Delete account closes the menu and opens the account sheet at the confirmation, without deleting.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task DeleteAccount_Tap_ShouldOpenTheSheetAtTheConfirmation()
	{
		var host = await OpenAccountMenuAsync(signedIn: true);

		await host.Find("#menuDeleteAccount").ClickAsync();

		var account = Resolve<AccountViewModel>();
		await host.WaitForAssertionAsync(() => account.IsConfirmingDelete.Should().BeTrue());
		account.IsOpen.Should().BeTrue();
		await host.WaitForAssertionAsync(() => host.FindAll("#appMenu").Should().BeEmpty());
		await Session.DidNotReceive().DeleteAccountAsync(Arg.Any<CancellationToken>());
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
