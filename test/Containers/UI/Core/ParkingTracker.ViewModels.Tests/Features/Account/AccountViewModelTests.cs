using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ParkingTracker.Application.Features.Accounts.Abstractions;
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
		return new AccountViewModel(client, google, ui.Toast);
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
		var noClient = () => new AccountViewModel(null!, google, ui.Toast); // null on purpose: proves the guard clause rejects it
		var noGoogle = () => new AccountViewModel(client, null!, ui.Toast); // null on purpose: proves the guard clause rejects it
		var noToast = () => new AccountViewModel(client, google, null!); // null on purpose: proves the guard clause rejects it

		noClient.Should().Throw<ArgumentNullException>();
		noGoogle.Should().Throw<ArgumentNullException>();
		noToast.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
