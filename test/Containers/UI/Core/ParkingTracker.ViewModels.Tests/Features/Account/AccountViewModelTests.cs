using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="AccountViewModel"/>: the signed-out and signed-in states of the account sheet, the plain words of each
///              sign-in failure, a session the API ended, the confirm step before the account is deleted, and the busy guard.
/// </summary>
public sealed class AccountViewModelTests : IDisposable
{
	#region Test Infrastructure
	/// <summary>
	/// The account the API returns.
	/// </summary>
	private static readonly MeResponse Me = new(Guid.Parse("0f3c2a1b-8d4e-4f60-9a7b-123456789abc"), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17), 2);

	/// <summary>
	/// The hash of the account the API returns.
	/// </summary>
	private static readonly string MeHash = DeviceOwnerHash.Of(Me.AccountId);

	/// <summary>
	/// The fixture that holds the toast.
	/// </summary>
	private readonly UiFixture ui = new();

	/// <summary>
	/// The substitute for the session client.
	/// </summary>
	private readonly ISessionClient client = Substitute.For<ISessionClient>();

	/// <summary>
	/// The substitute for Google sign-in.
	/// </summary>
	private readonly IGoogleSignIn google = Substitute.For<IGoogleSignIn>();

	/// <summary>
	/// Builds the view model over the substitutes.
	/// </summary>
	/// <param name="signedIn">Whether the phone holds a session.</param>
	/// <returns>The view model.</returns>
	private AccountViewModel Create(bool signedIn = false)
	{
		google.IsAvailable.Returns(true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(signedIn);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		client.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		client.DeleteAccountAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<string>(DeviceDataErrors.NotStored, ResultExceptionType.NotFound));
		ui.Owner.BindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		return ui.CreateAccount(client, google);
	}

	/// <inheritdoc />
	public void Dispose() => ui.Dispose();
	#endregion

	#region Refresh Tests
	/// <summary>
	/// Verifies that refreshing while signed in reads the account without opening the sheet.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_SignedIn_ShouldLoadTheAccountWithoutOpening()
	{
		var account = Create(signedIn: true);
		var raised = 0;
		account.Changed += (_, _) => raised++;

		await account.RefreshStateAsync();

		account.IsSignedIn.Should().BeTrue();
		account.AccountIdText.Should().Be("0f3c2a1b");
		account.IsOpen.Should().BeFalse();
		raised.Should().BeGreaterThan(0);
	}

	/// <summary>
	/// Verifies that refreshing while signed out does not call the API.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_SignedOut_ShouldNotCallTheApi()
	{
		var account = Create(signedIn: false);

		await account.RefreshStateAsync();

		account.IsSignedIn.Should().BeFalse();
		await client.DidNotReceive().GetMeAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a network error while refreshing keeps the session and shows no error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_NetworkError_ShouldKeepTheSessionAndShowNoError()
	{
		var account = Create(signedIn: true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<MeResponse>(AccountErrors.Network()));

		await account.RefreshStateAsync();

		account.IsSignedIn.Should().BeTrue();
		account.Account.Should().BeNull();
		account.Error.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a session the API ended shows as signed out when refreshing, without a message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_SessionEnded_ShouldShowSignedOut()
	{
		var account = Create(signedIn: true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<MeResponse>(AccountErrors.NotSignedIn()));

		await account.RefreshStateAsync();

		account.IsSignedIn.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that refreshing does nothing when the build cannot sign in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_NotAvailable_ShouldDoNothing()
	{
		var account = Create(signedIn: true);
		google.IsAvailable.Returns(false);

		await account.RefreshStateAsync();

		account.IsSignedIn.Should().BeFalse();
		await client.DidNotReceive().IsSignedInAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a secure storage that cannot be read does not throw, shows no error and still tells the menu to draw again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_StorageUnreadable_ShouldNotThrowAndShowNoError()
	{
		var account = Create(signedIn: true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new IOException());
		var raised = 0;
		account.Changed += (_, _) => raised++;

		var refresh = () => account.RefreshStateAsync();

		await refresh.Should().NotThrowAsync();
		account.Error.Should().BeEmpty();
		raised.Should().BeGreaterThan(0);
	}

	/// <summary>
	/// Verifies that opening to delete shows the confirmation while signed in, and only the sign-in while signed out.
	/// </summary>
	/// <param name="signedIn">Whether the phone holds a session.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task OpenToDeleteAsync_ShouldAskForTheConfirmationOnlyWhileSignedIn(bool signedIn)
	{
		var account = Create(signedIn);

		await account.OpenToDeleteAsync();

		account.IsOpen.Should().BeTrue();
		account.IsConfirmingDelete.Should().Be(signedIn);
		await client.DidNotReceive().DeleteAccountAsync(Arg.Any<CancellationToken>());
	}
	#endregion

	#region Open Tests
	/// <summary>
	/// Verifies that opening while signed out shows the sign-in state and does not call the API.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenAsync_SignedOut_ShouldShowSignInWithoutCallingTheApi()
	{
		var account = Create(signedIn: false);

		await account.OpenAsync();

		account.IsOpen.Should().BeTrue();
		account.IsSignedIn.Should().BeFalse();
		account.IsBusy.Should().BeFalse();
		await client.DidNotReceive().GetMeAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that opening while signed in reads the account and shows its details in words.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenAsync_SignedIn_ShouldShowTheAccount()
	{
		var account = Create(signedIn: true);

		await account.OpenAsync();

		account.IsSignedIn.Should().BeTrue();
		account.AccountIdText.Should().Be("0f3c2a1b");
		account.CreatedText.Should().Be("3 Nov 2026");
		account.LastSeenText.Should().Be("17 Nov 2026");
		account.ActiveSessionsText.Should().Be("2");
	}

	/// <summary>
	/// Verifies that a session the API ended is shown as signed out with a reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenAsync_SessionEnded_ShouldShowSignedOutWithAReason()
	{
		var account = Create(signedIn: true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<MeResponse>(AccountErrors.NotSignedIn()));

		await account.OpenAsync();

		account.IsSignedIn.Should().BeFalse();
		account.Error.Should().Be(AccountViewModel.SessionEndedText);
	}

	/// <summary>
	/// Verifies that a network error while reading the account keeps the session and shows the reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenAsync_NetworkError_ShouldKeepTheSessionAndShowTheReason()
	{
		var account = Create(signedIn: true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<MeResponse>(AccountErrors.Network()));

		await account.OpenAsync();

		account.IsSignedIn.Should().BeTrue();
		account.Account.Should().BeNull();
		account.Error.Should().Be(AccountErrors.Network().Message);
	}

	/// <summary>
	/// Verifies that closing hides the sheet and drops a pending confirmation.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Close_WhileConfirming_ShouldCloseAndForgetTheConfirmation()
	{
		var account = Create(signedIn: true);
		await account.OpenAsync();
		account.RequestDelete();

		account.Close();

		account.IsOpen.Should().BeFalse();
		account.IsConfirmingDelete.Should().BeFalse();
	}
	#endregion

	#region Sign In Tests
	/// <summary>
	/// Verifies that a successful sign-in shows the account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_Succeeds_ShouldShowTheAccount()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		await account.OpenAsync();

		await account.SignInAsync();

		account.IsSignedIn.Should().BeTrue();
		account.Account.Should().Be(Me);
		account.Error.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that each sign-in failure is shown in its own plain words and leaves the user signed out.
	/// </summary>
	/// <param name="code">The code of the failure.</param>
	/// <param name="expectedText">The words the user should read.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(AccountErrors.CancelledCode, "Sign-in was cancelled.")]
	[InlineData(AccountErrors.NoAccountCode, "There is no Google account on this phone. Add one in the phone settings and try again.")]
	[InlineData(AccountErrors.UnavailableCode, "Google sign-in is not available in this build or on this phone.")]
	[InlineData(AccountErrors.NetworkCode, "The server could not be reached. Check your connection and try again.")]
	public async Task SignInAsync_Fails_ShouldShowTheReasonInWords(string code, string expectedText)
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error(code, expectedText)));
		await account.OpenAsync();

		await account.SignInAsync();

		account.IsSignedIn.Should().BeFalse();
		account.Error.Should().Be(expectedText);
		account.IsBusy.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the messages of the real failures are distinct plain words.
	/// </summary>
	[Fact]
	public void Errors_ShouldCarryDistinctPlainMessages()
	{
		new[] { AccountErrors.Cancelled(), AccountErrors.NoAccount(), AccountErrors.Unavailable(), AccountErrors.Network() }
			.Select(error => error.Message).Should().OnlyHaveUniqueItems();
	}

	/// <summary>
	/// Verifies that a second tap while a sign-in runs does nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_WhileBusy_ShouldRunOnce()
	{
		var account = Create();
		var gate = new TaskCompletionSource<Result>();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);
		await account.OpenAsync();

		var first = account.SignInAsync();
		await account.SignInAsync();
		gate.SetResult(Result.Failure(AccountErrors.Cancelled()));
		await first;

		await client.Received(1).SignInAsync(Arg.Any<CancellationToken>());
	}
	#endregion

	#region Sign Out And Delete Tests
	/// <summary>
	/// Verifies that signing out shows the signed-out state.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_ShouldShowSignedOut()
	{
		var account = Create(signedIn: true);
		await account.OpenAsync();

		await account.SignOutAsync();

		account.IsSignedIn.Should().BeFalse();
		account.Account.Should().BeNull();
		await client.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a confirmed request deletes the account, shows the toast and the signed-out state.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_AfterRequest_ShouldDeleteAndShowSignedOut()
	{
		var account = Create(signedIn: true);
		await account.OpenAsync();

		account.RequestDelete();
		account.IsConfirmingDelete.Should().BeTrue();
		await client.DidNotReceive().DeleteAccountAsync(Arg.Any<CancellationToken>());
		await account.ConfirmDeleteAsync();

		await client.Received(1).DeleteAccountAsync(Arg.Any<CancellationToken>());
		account.IsSignedIn.Should().BeFalse();
		ui.Toast.Message.Should().Be(AccountViewModel.DeletedText);
	}

	/// <summary>
	/// Verifies that a confirmation that was never asked for deletes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_WithoutRequest_ShouldDeleteNothing()
	{
		var account = Create(signedIn: true);
		await account.OpenAsync();

		await account.ConfirmDeleteAsync();

		await client.DidNotReceive().DeleteAccountAsync(Arg.Any<CancellationToken>());
		account.IsSignedIn.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that withdrawing the request keeps the account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CancelDelete_ShouldKeepTheAccount()
	{
		var account = Create(signedIn: true);
		await account.OpenAsync();
		account.RequestDelete();

		account.CancelDelete();
		await account.ConfirmDeleteAsync();

		account.IsConfirmingDelete.Should().BeFalse();
		await client.DidNotReceive().DeleteAccountAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a deletion the server did not confirm keeps the session and shows the reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_Fails_ShouldKeepTheSessionAndShowTheReason()
	{
		var account = Create(signedIn: true);
		client.DeleteAccountAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(AccountErrors.Network()));
		await account.OpenAsync();
		account.RequestDelete();

		await account.ConfirmDeleteAsync();

		account.IsSignedIn.Should().BeTrue();
		account.Error.Should().Be(AccountErrors.Network().Message);
		account.IsConfirmingDelete.Should().BeFalse();
	}
	#endregion

	#region Device Owner Tests
	/// <summary>
	/// Verifies that the hash is the lowercase hexadecimal SHA-256 of the account id in the "N" format.
	/// </summary>
	[Fact]
	public void DeviceOwnerHash_ShouldBeLowercaseHexSha256OfTheNFormat()
	{
		var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("0f3c2a1b8d4e4f609a7b123456789abc")));

		MeHash.Should().Be(expected).And.HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
	}

	/// <summary>
	/// Verifies that the first sign-in on a phone binds the account and keeps the data already on the phone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_NoOwner_ShouldBindTheAccountAndKeepTheData()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

		await account.SignInAsync();

		await ui.Owner.Received(1).BindAsync(MeHash, Arg.Any<CancellationToken>());
		account.IsConfirmingOwnerChange.Should().BeFalse();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that a sign-in with the bound account changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_SameOwner_ShouldChangeNothing()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(MeHash));

		await account.SignInAsync();

		account.IsConfirmingOwnerChange.Should().BeFalse();
		await ui.Owner.DidNotReceiveWithAnyArgs().BindAsync(default!);
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that another account waits for the decision of the user and nothing is removed or bound before it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_OtherOwner_ShouldAskBeforeRemovingAnything()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));

		await account.SignInAsync();

		account.IsConfirmingOwnerChange.Should().BeTrue();
		account.IsSignedIn.Should().BeTrue();
		await ui.Owner.DidNotReceiveWithAnyArgs().BindAsync(default!);
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that "Remove and continue" wipes the profile, binds the new account and refreshes what the screens show.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmOwnerChangeAsync_ShouldWipeTheProfileBindAndReloadTheScreens()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		await account.SignInAsync();
		var noticed = 0;
		ui.ResetNotice.StaleRowsRemoved += (_, _) => noticed++;
		ui.PlacesStore.ClearReceivedCalls();

		await account.ConfirmOwnerChangeAsync();

		await ui.Wiper.Received(1).WipeProfileAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
		await ui.Owner.Received(1).BindAsync(MeHash, Arg.Any<CancellationToken>());
		account.IsConfirmingOwnerChange.Should().BeFalse();
		account.IsSignedIn.Should().BeTrue();
		noticed.Should().Be(1);
		await ui.PlacesStore.Received(1).GetPlacesAsync(Arg.Any<CancellationToken>());
		await ui.VehicleStore.Received().GetAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a wipe that failed keeps the question, binds nothing and says why.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmOwnerChangeAsync_WipeFails_ShouldKeepTheQuestionAndBindNothing()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		ui.Wiper.WipeProfileAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")));
		await account.SignInAsync();

		await account.ConfirmOwnerChangeAsync();

		account.IsConfirmingOwnerChange.Should().BeTrue();
		account.Error.Should().Be(AccountViewModel.OwnerChangeFailedText);
		await ui.Owner.DidNotReceiveWithAnyArgs().BindAsync(default!);
	}

	/// <summary>
	/// Verifies that "Cancel" signs the new account out and leaves the data and the binding alone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CancelOwnerChangeAsync_ShouldSignOutAndKeepTheData()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		await account.SignInAsync();

		await account.CancelOwnerChangeAsync();

		await client.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
		account.IsSignedIn.Should().BeFalse();
		account.IsConfirmingOwnerChange.Should().BeFalse();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
		await ui.Owner.DidNotReceiveWithAnyArgs().BindAsync(default!);
	}

	/// <summary>
	/// Verifies that closing the sheet does not drop the question about the data of the other account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Close_WhileAskingAboutOtherData_ShouldKeepTheQuestion()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		await account.OpenAsync();
		await account.SignInAsync();

		account.Close();

		account.IsConfirmingOwnerChange.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that an owner that cannot be read shows a message and neither binds nor removes anything.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_OwnerUnreadable_ShouldSignTheNewAccountOutAndShowTheReason()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<string>(new Error("x", "x")));

		await account.SignInAsync();

		account.IsSignedIn.Should().BeFalse();
		account.IsOwnerVerified.Should().BeFalse();
		account.Error.Should().Be(AccountViewModel.OwnerUnknownText);
		await client.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Owner.DidNotReceiveWithAnyArgs().BindAsync(default!);
		account.IsConfirmingOwnerChange.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a binding that failed right after the sign-in also signs the new account out.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_BindFails_ShouldSignTheNewAccountOut()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.BindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")));

		await account.SignInAsync();

		account.IsSignedIn.Should().BeFalse();
		account.Error.Should().Be(AccountViewModel.OwnerUnknownText);
	}

	/// <summary>
	/// Verifies that the owner counts as verified after the first binding and after a sign-in with the bound account, and not before.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task IsOwnerVerified_ShouldBeTrueOnlyForTheBoundAccount()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		account.IsOwnerVerified.Should().BeFalse();

		await account.SignInAsync();

		account.IsOwnerVerified.Should().BeTrue();
		await account.SignOutAsync();
		account.IsOwnerVerified.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a restart (a new view model, a menu refresh) with the data of another account on the phone opens the sheet in the question.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_OtherOwner_ShouldOpenTheSheetWithTheQuestion()
	{
		var account = Create(signedIn: true);
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));

		await account.RefreshStateAsync();

		account.IsOpen.Should().BeTrue();
		account.IsConfirmingOwnerChange.Should().BeTrue();
		account.IsOwnerVerified.Should().BeFalse();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that closing the question and opening the sheet again compares again and asks again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenAsync_AfterClosingTheQuestion_ShouldAskAgain()
	{
		var account = Create(signedIn: true);
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		await account.RefreshStateAsync();
		account.Close();

		await account.OpenAsync();

		account.IsConfirmingOwnerChange.Should().BeTrue();
		await ui.Owner.Received(2).GetAccountHashAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a refresh with the bound account verifies the owner and does not open the sheet.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_SameOwner_ShouldVerifyWithoutOpening()
	{
		var account = Create(signedIn: true);
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(MeHash));

		await account.RefreshStateAsync();

		account.IsOwnerVerified.Should().BeTrue();
		account.IsOpen.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that an owner that cannot be read while refreshing keeps the session, shows nothing and leaves the owner unverified.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_OwnerUnreadable_ShouldStayAsItIsAndShowNothing()
	{
		var account = Create(signedIn: true);
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<string>(new Error("x", "x")));

		await account.RefreshStateAsync();

		account.IsSignedIn.Should().BeTrue();
		account.IsOwnerVerified.Should().BeFalse();
		account.IsConfirmingOwnerChange.Should().BeFalse();
		account.IsOpen.Should().BeFalse();
		account.Error.Should().BeEmpty();
		await client.DidNotReceive().SignOutAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a "Cancel" whose sign-out fails keeps the question, shows the reason and binds nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CancelOwnerChangeAsync_SignOutFails_ShouldKeepTheQuestionAndBindNothing()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		await account.SignInAsync();
		client.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(AccountErrors.Network()));

		await account.CancelOwnerChangeAsync();

		account.IsSignedIn.Should().BeTrue();
		account.IsConfirmingOwnerChange.Should().BeTrue();
		account.Error.Should().Be(AccountErrors.Network().Message);
		await ui.Owner.DidNotReceiveWithAnyArgs().BindAsync(default!);
	}

	/// <summary>
	/// Verifies that a removal that was pending when the app stopped runs again when the signed-out state is refreshed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_SignedOutWithPendingRemoval_ShouldWipeEverything()
	{
		var account = Create(signedIn: false);
		ui.Owner.GetErasePendingAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(true));

		await account.RefreshStateAsync();

		await ui.Wiper.Received(1).WipeAllAsync(Arg.Any<CancellationToken>());
		account.IsWipePending.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that opening the signed-out sheet finishes a pending removal too, and that a failure leaves the retry.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenAsync_SignedOutWithPendingRemovalThatFails_ShouldOfferTheRetry()
	{
		var account = Create(signedIn: false);
		ui.Owner.GetErasePendingAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(true));
		ui.Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")));

		await account.OpenAsync();

		account.IsWipePending.Should().BeTrue();
		account.Error.Should().Be(AccountViewModel.WipeFailedText);
	}

	/// <summary>
	/// Verifies that a signed-out phone without a pending removal wipes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_SignedOutWithoutPendingRemoval_ShouldWipeNothing()
	{
		var account = Create(signedIn: false);

		await account.RefreshStateAsync();

		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
	}

	/// <summary>
	/// Verifies that a deleted account takes everything from the phone before the toast.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_ShouldWipeEverythingFromThePhone()
	{
		var account = Create(signedIn: true);
		await account.OpenAsync();
		account.RequestDelete();

		await account.ConfirmDeleteAsync();

		await ui.Wiper.Received(1).WipeAllAsync(Arg.Any<CancellationToken>());
		await ui.Alarms.Received(1).CancelAsync(Arg.Any<CancellationToken>());
		account.IsWipePending.Should().BeFalse();
		account.Error.Should().BeEmpty();
		ui.Toast.Message.Should().Be(AccountViewModel.DeletedText);
	}

	/// <summary>
	/// Verifies that a refused deletion on the server wipes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_ServerRefuses_ShouldWipeNothing()
	{
		var account = Create(signedIn: true);
		client.DeleteAccountAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(AccountErrors.Network()));
		await account.OpenAsync();
		account.RequestDelete();

		await account.ConfirmDeleteAsync();

		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
	}

	/// <summary>
	/// Verifies that a wipe that failed after the deletion is shown, signs out, and can be retried.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_WipeFails_ShouldShowTheErrorAndAllowARetry()
	{
		var account = Create(signedIn: true);
		ui.Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")), Result.Success());
		await account.OpenAsync();
		account.RequestDelete();

		await account.ConfirmDeleteAsync();

		account.IsSignedIn.Should().BeFalse();
		account.IsWipePending.Should().BeTrue();
		account.Error.Should().Be(AccountViewModel.WipeFailedText);
		await ui.Alarms.DidNotReceiveWithAnyArgs().CancelAsync();

		await account.RetryWipeAsync();

		account.IsWipePending.Should().BeFalse();
		account.Error.Should().BeEmpty();
		await ui.Wiper.Received(2).WipeAllAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a retry without a pending wipe does nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RetryWipeAsync_NothingPending_ShouldDoNothing()
	{
		var account = Create();

		await account.RetryWipeAsync();

		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
	}

	/// <summary>
	/// Makes the owner store of the fixture keep the pending-removal flag in a variable and the wiper clear it, as the real ones do.
	/// </summary>
	/// <returns>A function that reads the flag.</returns>
	private Func<bool> KeepTheFlag()
	{
		var flag = false;
		ui.Owner.MarkErasePendingAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			flag = true;
			return Result.Success();
		});
		ui.Owner.GetErasePendingAsync(Arg.Any<CancellationToken>()).Returns(_ => Result.Success(flag));
		ui.Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			flag = false;
			return Result.Success();
		});
		return () => flag;
	}

	/// <summary>
	/// Verifies that a deletion the server confirmed sets the flag, then wipes, in that order, and that the wipe clears the flag.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_ServerConfirms_ShouldDeleteThenSetTheFlagThenWipe()
	{
		var account = Create(signedIn: true);
		var flag = KeepTheFlag();
		var order = new List<string>();
		client.DeleteAccountAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			order.Add("delete");
			return Result.Success();
		});
		ui.Owner.MarkErasePendingAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			order.Add("flag");
			return Result.Success();
		});
		ui.Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			order.Add("wipe");
			return Result.Success();
		});
		await account.OpenAsync();
		account.RequestDelete();

		await account.ConfirmDeleteAsync();

		order.Should().Equal("delete", "flag", "wipe");
		flag().Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a wipe that failed after the confirmed deletion leaves the flag, and that the next refresh wipes again, signed in or out.
	/// </summary>
	/// <param name="signedIn">Whether the session client still says the phone holds a session at the next refresh.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RefreshStateAsync_AfterConfirmedDeleteWithFailedWipe_ShouldWipeAgain(bool signedIn)
	{
		var account = Create(signedIn: true);
		var flag = KeepTheFlag();
		ui.Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")));
		await account.OpenAsync();
		account.RequestDelete();
		await account.ConfirmDeleteAsync();
		flag().Should().BeTrue();
		ui.Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(signedIn);

		await account.RefreshStateAsync();

		await ui.Wiper.Received(2).WipeAllAsync(Arg.Any<CancellationToken>());
		account.IsWipePending.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a flag that cannot be written does not stop the wipe.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_FlagNotWritten_ShouldStillWipe()
	{
		var account = Create(signedIn: true);
		ui.Owner.MarkErasePendingAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("x", "x")));
		await account.OpenAsync();
		account.RequestDelete();

		await account.ConfirmDeleteAsync();

		await ui.Wiper.Received(1).WipeAllAsync(Arg.Any<CancellationToken>());
		account.IsSignedIn.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a deletion the server refused touches neither the flag nor the data.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_ServerRefuses_ShouldNotSetTheFlag()
	{
		var account = Create(signedIn: true);
		client.DeleteAccountAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(AccountErrors.Network()));
		await account.OpenAsync();
		account.RequestDelete();

		await account.ConfirmDeleteAsync();

		await ui.Owner.DidNotReceiveWithAnyArgs().MarkErasePendingAsync();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
	}

	/// <summary>
	/// Verifies that a deletion that throws touches neither the flag nor the data, and that a later refresh wipes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmDeleteAsync_Throws_ShouldNotSetTheFlagAndAllowLaterRefreshes()
	{
		var account = Create(signedIn: true);
		KeepTheFlag();
		client.DeleteAccountAsync(Arg.Any<CancellationToken>()).Returns<Task<Result>>(_ => throw new InvalidOperationException("network"));
		await account.OpenAsync();
		account.RequestDelete();

		var delete = () => account.ConfirmDeleteAsync();
		await delete.Should().ThrowAsync<InvalidOperationException>();
		await account.RefreshStateAsync();

		account.IsBusy.Should().BeFalse();
		await ui.Owner.DidNotReceiveWithAnyArgs().MarkErasePendingAsync();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
	}

	/// <summary>
	/// Verifies that a session the API ended wipes nothing when no deletion was confirmed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_SessionEndedWithoutFlag_ShouldWipeNothing()
	{
		var account = Create(signedIn: true);
		KeepTheFlag();
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<MeResponse>(AccountErrors.NotSignedIn()));

		await account.RefreshStateAsync();

		account.IsSignedIn.Should().BeFalse();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
	}

	/// <summary>
	/// Verifies that a sign-in that is started while a refresh runs waits for it and then runs, instead of being dropped.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_WhileARefreshRuns_ShouldRunAfterTheRefresh()
	{
		var account = Create(signedIn: false);
		var gate = new TaskCompletionSource<bool>();
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		var busyAtChange = new List<bool>();
		account.Changed += (_, _) => busyAtChange.Add(account.IsBusy);
		var refresh = account.RefreshStateAsync();
		account.IsBusy.Should().BeTrue();
		busyAtChange.Should().Equal(true);

		var signIn = account.SignInAsync();
		await client.DidNotReceive().SignInAsync(Arg.Any<CancellationToken>());
		gate.SetResult(false);
		await refresh;
		await signIn;

		await client.Received(1).SignInAsync(Arg.Any<CancellationToken>());
		account.IsSignedIn.Should().BeTrue();
		account.IsBusy.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a refresh that starts while a sign-in is running changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_WhileASignInRuns_ShouldChangeNothing()
	{
		var account = Create(signedIn: false);
		var gate = new TaskCompletionSource<Result>();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
		var signIn = account.SignInAsync();
		client.ClearReceivedCalls();

		await account.RefreshStateAsync();
		gate.SetResult(Result.Success());
		await signIn;

		await client.DidNotReceive().IsSignedInAsync(Arg.Any<CancellationToken>());
		account.IsSignedIn.Should().BeTrue();
		account.IsOwnerVerified.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that opening the sheet straight into the delete prompt ends in the prompt even when a refresh was running.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenToDeleteAsync_WhileARefreshRuns_ShouldShowThePrompt()
	{
		var account = Create(signedIn: true);
		var gate = new TaskCompletionSource<bool>();
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
		var refresh = account.RefreshStateAsync();

		var open = account.OpenToDeleteAsync();
		gate.SetResult(true);
		await refresh;
		await open;

		account.IsOpen.Should().BeTrue();
		account.IsConfirmingDelete.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a refresh that throws leaves the view model free for the next action.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_Throws_ShouldLeaveItNotBusy()
	{
		var account = Create(signedIn: true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns<Task<bool>>(_ => throw new InvalidOperationException("boom"));
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

		var refresh = () => account.RefreshStateAsync();
		await refresh.Should().ThrowAsync<InvalidOperationException>();
		await account.SignInAsync();

		account.IsBusy.Should().BeFalse();
		await client.Received(1).SignInAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a refresh that is cancelled leaves the view model free for the next action.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_Cancelled_ShouldLeaveItNotBusy()
	{
		var account = Create(signedIn: true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(call => Task.FromCanceled<bool>(call.Arg<CancellationToken>()));
		using var source = new CancellationTokenSource();
		await source.CancelAsync();

		var refresh = () => account.RefreshStateAsync(source.Token);
		await refresh.Should().ThrowAsync<OperationCanceledException>();

		account.IsBusy.Should().BeFalse();
	}
	#endregion

	#region Storage Failure And Cancellation Tests
	/// <summary>
	/// Verifies that failed secure token persistence shows recovery instructions and resets the busy state.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_StorageWriteFails_ShouldShowRecoveryInstructions()
	{
		var account = Create();
		client.SignInAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<Result>(new IOException("private storage failure")));
		await account.OpenAsync();

		await account.SignInAsync();

		account.IsBusy.Should().BeFalse();
		account.IsSignedIn.Should().BeFalse();
		account.Error.Should().Contain("Restart the app and try again.").And.NotContain("private storage failure");
		await client.DidNotReceive().GetMeAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that failed secure token removal keeps the account visible and reports how to retry.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_StorageClearFails_ShouldKeepAccountAndShowRecoveryInstructions()
	{
		var account = Create(signedIn: true);
		await account.OpenAsync();
		client.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<Result>(new IOException("private storage failure")));

		await account.SignOutAsync();

		account.IsBusy.Should().BeFalse();
		account.IsSignedIn.Should().BeTrue();
		account.Account.Should().Be(Me);
		account.Error.Should().Contain("Restart the app and try again.").And.NotContain("private storage failure");
	}

	/// <summary>
	/// Verifies that caller cancellation reaches the session client and propagates without a storage error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_CallerCancels_ShouldPropagateCancellation()
	{
		var account = Create();
		await account.OpenAsync();
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		client.SignInAsync(cancellation.Token).Returns(Task.FromCanceled<Result>(cancellation.Token));

		var signIn = () => account.SignInAsync(cancellation.Token);

		await signIn.Should().ThrowAsync<OperationCanceledException>();
		account.IsBusy.Should().BeFalse();
		account.Error.Should().BeEmpty();
		await client.Received(1).SignInAsync(cancellation.Token);
	}
	#endregion

	#region Availability Tests
	/// <summary>
	/// Verifies that the availability follows Google sign-in, so that a build without a Web client ID offers nothing.
	/// </summary>
	[Fact]
	public void IsAvailable_ShouldFollowGoogleSignIn()
	{
		var account = Create();
		google.IsAvailable.Returns(false);

		account.IsAvailable.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a missing dependency is refused.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		var noClient = () => new AccountViewModel(null!, google, ui.Toast, ui.Owner, ui.Eraser, ui.Verification, ui.Sync, ui.LocalProfile, ui.Clock); // null on purpose: proves the guard clause rejects it
		var noGoogle = () => new AccountViewModel(client, null!, ui.Toast, ui.Owner, ui.Eraser, ui.Verification, ui.Sync, ui.LocalProfile, ui.Clock); // null on purpose: proves the guard clause rejects it
		var noToast = () => new AccountViewModel(client, google, null!, ui.Owner, ui.Eraser, ui.Verification, ui.Sync, ui.LocalProfile, ui.Clock); // null on purpose: proves the guard clause rejects it
		var noOwner = () => new AccountViewModel(client, google, ui.Toast, null!, ui.Eraser, ui.Verification, ui.Sync, ui.LocalProfile, ui.Clock); // null on purpose: proves the guard clause rejects it
		var noEraser = () => new AccountViewModel(client, google, ui.Toast, ui.Owner, null!, ui.Verification, ui.Sync, ui.LocalProfile, ui.Clock); // null on purpose: proves the guard clause rejects it

		noClient.Should().Throw<ArgumentNullException>();
		noGoogle.Should().Throw<ArgumentNullException>();
		noToast.Should().Throw<ArgumentNullException>();
		noOwner.Should().Throw<ArgumentNullException>();
		noEraser.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
