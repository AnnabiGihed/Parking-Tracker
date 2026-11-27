using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.Shared.Features.Account.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Domain.Shared;
using Radzen.Blazor;

namespace ParkingTracker.Shared.Tests.Features.Account.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the account sheet: it opens as a Radzen bottom sheet through the bridge, offers "Sign in with Google" while
///              signed out and shows each failure in words, shows the account with Sign out and Delete account while signed in, asks for a confirmation before
///              deleting, and closes with the back button of the phone.
/// </summary>
public sealed class AccountSheetTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// The account the API returns.
	/// </summary>
	private static readonly MeResponse Me = new(Guid.Parse("0f3c2a1b-8d4e-4f60-9a7b-123456789abc"), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17), 2);

	/// <summary>
	/// Renders the Radzen host and the sheet, and opens it.
	/// </summary>
	/// <param name="signedIn">Whether the phone holds a session.</param>
	/// <returns>The Radzen dialog host, which holds the sheet while it is open.</returns>
	private async Task<IRenderedComponent<RadzenDialog>> OpenAsync(bool signedIn)
	{
		Google.IsAvailable.Returns(true);
		Session.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(signedIn);
		Session.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		Session.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		Session.DeleteAccountAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		var host = Render<RadzenDialog>();
		Render<AccountSheet>();

		await host.InvokeAsync(Resolve<AccountViewModel>().OpenAsync).ConfigureAwait(false);

		await host.WaitForAssertionAsync(() => host.FindAll("#accountSheet").Should().HaveCount(1)).ConfigureAwait(false);
		return host;
	}

	/// <summary>
	/// Signs in with an account other than the one the phone holds the data of, and opens the sheet.
	/// </summary>
	/// <returns>The Radzen dialog host with the question about the data.</returns>
	private async Task<IRenderedComponent<RadzenDialog>> SignInAsOtherAccountAsync()
	{
		Session.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		var host = await OpenAsync(signedIn: false).ConfigureAwait(false);
		await host.Find("#accountSignIn").ClickAsync().ConfigureAwait(false);
		await host.WaitForAssertionAsync(() => host.FindAll("#accountOwnerChangeConfirm").Should().HaveCount(1)).ConfigureAwait(false);
		return host;
	}
	#endregion

	#region Signed Out Tests
	/// <summary>
	/// Verifies that the sheet opens as a bottom sheet with its title, the back button closes it, and while signed out it offers only "Sign in with Google".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_SignedOut_ShouldOfferOnlySignIn()
	{
		var host = await OpenAsync(signedIn: false);

		host.Find(".rz-dialog-title").TextContent.Should().Be("Account");
		host.FindAll(".rz-dialog.app-sheet").Should().HaveCount(1);
		host.Find("#accountSignIn").TextContent.Should().Contain("Sign in with Google");
		host.FindAll("#accountSignOut, #accountDelete, #accountId").Should().BeEmpty();
		Resolve<IBackNavigation>().CanGoBack.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a tap on "Sign in with Google" signs in and then shows the account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignIn_Tap_ShouldShowTheAccount()
	{
		Session.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		var host = await OpenAsync(signedIn: false);

		await host.Find("#accountSignIn").ClickAsync();

		await host.WaitForAssertionAsync(() => host.Find("#accountId").TextContent.Should().Be("0f3c2a1b"));
		host.Find("#accountCreated").TextContent.Should().Be("3 Nov 2026");
		host.Find("#accountLastSeen").TextContent.Should().Be("17 Nov 2026");
		host.Find("#accountSessions").TextContent.Should().Be("2");
		host.FindAll("#accountSignIn").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that each failure of the sign-in is shown in plain words and the sign-in button stays.
	/// </summary>
	/// <param name="code">The code of the failure.</param>
	/// <param name="message">The words the user should read.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(AccountErrors.CancelledCode, "Sign-in was cancelled.")]
	[InlineData(AccountErrors.NoAccountCode, "There is no Google account on this phone. Add one in the phone settings and try again.")]
	[InlineData(AccountErrors.UnavailableCode, "Google sign-in is not available in this build or on this phone.")]
	[InlineData(AccountErrors.NetworkCode, "The server could not be reached. Check your connection and try again.")]
	public async Task SignIn_Fails_ShouldShowTheReasonInWords(string code, string message)
	{
		Session.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error(code, message)));
		var host = await OpenAsync(signedIn: false);

		await host.Find("#accountSignIn").ClickAsync();

		await host.WaitForAssertionAsync(() => host.Find("#accountError").TextContent.Trim().Should().Be(message));
		host.FindAll("#accountSignIn").Should().HaveCount(1);
	}
	#endregion

	#region Other Account Tests
	/// <summary>
	/// Verifies that signing in with another account asks whether to remove the data of the previous one, with the words of the design, and offers nothing else.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignIn_OtherAccount_ShouldAskBeforeRemovingTheData()
	{
		var host = await SignInAsOtherAccountAsync();

		host.Find("#accountOwnerChangeNote").TextContent.Should().Be("This phone holds the places, vehicle and card of another account. Remove them to continue with this account?");
		host.Find("#accountOwnerChangeConfirm").TextContent.Should().Contain("Remove and continue");
		host.Find("#accountOwnerChangeCancel").TextContent.Should().Contain("Cancel");
		host.FindAll("#accountSignOut, #accountDelete").Should().BeEmpty();
		await Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that "Remove and continue" removes the profile, binds the account and shows the account again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OwnerChange_Confirm_ShouldWipeTheProfileAndShowTheAccount()
	{
		var host = await SignInAsOtherAccountAsync();

		await host.Find("#accountOwnerChangeConfirm").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#accountSignOut").Should().HaveCount(1));
		await Wiper.Received(1).WipeProfileAsync(Arg.Any<CancellationToken>());
		await Owner.Received(1).BindAsync(DeviceOwnerHash.Of(Me.AccountId), Arg.Any<CancellationToken>());
		host.FindAll("#accountOwnerChangeConfirm").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that "Cancel" signs the new account out and keeps the data.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OwnerChange_Cancel_ShouldSignOutAndKeepTheData()
	{
		var host = await SignInAsOtherAccountAsync();

		await host.Find("#accountOwnerChangeCancel").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#accountSignIn").Should().HaveCount(1));
		await Session.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
		await Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that a wipe that failed after the deletion shows the error line and a button that tries again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Delete_WipeFails_ShouldShowTheErrorAndRetryRemovesTheData()
	{
		Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")), Result.Success());
		var host = await OpenAsync(signedIn: true);
		await host.Find("#accountDelete").ClickAsync();
		await host.Find("#accountDeleteConfirm").ClickAsync();

		await host.WaitForAssertionAsync(() => host.Find("#accountError").TextContent.Trim().Should().Be(AccountViewModel.WipeFailedText));
		host.FindAll("#accountWipeRetry").Should().HaveCount(1);

		await host.Find("#accountWipeRetry").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#accountWipeRetry, #accountError").Should().BeEmpty());
		await Wiper.Received(2).WipeAllAsync(Arg.Any<CancellationToken>());
	}
	#endregion

	#region Signed In Tests
	/// <summary>
	/// Verifies that a signed-in sheet shows the account and offers Sign out and Delete account, not the sign-in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_SignedIn_ShouldShowTheAccountAndItsActions()
	{
		var host = await OpenAsync(signedIn: true);

		host.Find("#accountId").TextContent.Should().Be("0f3c2a1b");
		host.FindAll("#accountSignOut").Should().HaveCount(1);
		host.FindAll("#accountDelete").Should().HaveCount(1);
		host.FindAll("#accountSignIn, #accountDeleteConfirm").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a tap on "Sign out" signs out and shows the sign-in again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOut_Tap_ShouldShowSignIn()
	{
		var host = await OpenAsync(signedIn: true);

		await host.Find("#accountSignOut").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#accountSignIn").Should().HaveCount(1));
		await Session.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "Delete account" only asks for a confirmation, and that the account is deleted only after the confirmation.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Delete_Tap_ShouldAskThenDeleteOnConfirm()
	{
		var host = await OpenAsync(signedIn: true);

		await host.Find("#accountDelete").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#accountDeleteConfirm").Should().HaveCount(1));
		await Session.DidNotReceive().DeleteAccountAsync(Arg.Any<CancellationToken>());
		host.FindAll("#accountDelete, #accountSignOut").Should().BeEmpty();

		await host.Find("#accountDeleteConfirm").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#accountSignIn").Should().HaveCount(1));
		await Session.Received(1).DeleteAccountAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that backing out of the confirmation keeps the account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Delete_Cancel_ShouldKeepTheAccount()
	{
		var host = await OpenAsync(signedIn: true);
		await host.Find("#accountDelete").ClickAsync();

		await host.Find("#accountDeleteCancel").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#accountDelete").Should().HaveCount(1));
		await Session.DidNotReceive().DeleteAccountAsync(Arg.Any<CancellationToken>());
	}
	#endregion
}
