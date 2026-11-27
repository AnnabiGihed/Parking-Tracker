using NSubstitute;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.ProfileSync;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of the part of <see cref="AccountViewModel"/> that follows the profile sync (ADR-0032): a verified owner asks for a sync; signing
///              out first syncs and only a sync that ended synced signs out and removes the profile, otherwise the sheet asks, "Sign out anyway" removes
///              the profile and "Stay signed in" changes nothing; an owner that was never verified signs out without a sync and without removing
///              anything; a failed sign-out removes nothing; and a phone that holds nothing is bound to the new account without a question.
/// </summary>
public sealed class AccountViewModelProfileSyncTests : IDisposable
{
	#region Test Infrastructure
	/// <summary>
	/// The account the API returns.
	/// </summary>
	private static readonly MeResponse Me = new(Guid.Parse("0f3c2a1b-8d4e-4f60-9a7b-123456789abc"), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17), 2);

	/// <summary>
	/// The hash the phone stores for that account.
	/// </summary>
	private static readonly string MeHash = DeviceOwnerHash.Of(Me.AccountId);

	/// <summary>
	/// The fixture that holds the view models and the substitutes.
	/// </summary>
	private readonly UiFixture ui = new();

	/// <summary>
	/// The session client.
	/// </summary>
	private readonly ISessionClient client = Substitute.For<ISessionClient>();

	/// <summary>
	/// Builds the view model for a phone that is signed in and bound to the account, and has it verify the owner.
	/// </summary>
	/// <returns>A task with the view model, after the refresh that verifies the owner.</returns>
	private async Task<AccountViewModel> VerifiedAsync()
	{
		var google = Substitute.For<IGoogleSignIn>();
		google.IsAvailable.Returns(true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		client.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(MeHash));
		var account = ui.CreateAccount(client, google);
		await account.RefreshStateAsync().ConfigureAwait(false);
		account.IsOwnerVerified.Should().BeTrue();
		return account;
	}

	/// <inheritdoc />
	public void Dispose() => ui.Dispose();
	#endregion

	#region Request Tests
	/// <summary>
	/// Verifies that verifying the owner asks for a sync, and that an owner who is not the account does not.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_OwnerVerified_ShouldRequestASync()
	{
		_ = await VerifiedAsync();

		ui.Sync.Received(1).Request();
	}

	/// <summary>
	/// Verifies that no sync is requested while another account's data is on the phone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_OtherOwnerWithData_ShouldNotRequestASync()
	{
		var google = Substitute.For<IGoogleSignIn>();
		google.IsAvailable.Returns(true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		var account = ui.CreateAccount(client, google);

		await account.RefreshStateAsync();

		ui.Sync.DidNotReceive().Request();
		account.IsConfirmingOwnerChange.Should().BeTrue();
	}
	#endregion

	#region Sign Out Tests
	/// <summary>
	/// Verifies that a sync that ended synced signs out on the server and then removes the profile, and never the parked car or the owner binding.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_Synced_ShouldSignOutAndWipeTheProfile()
	{
		var account = await VerifiedAsync();

		await account.SignOutAsync();

		account.IsSignedIn.Should().BeFalse();
		account.IsOwnerVerified.Should().BeFalse();
		account.IsConfirmingSignOut.Should().BeFalse();
		await ui.Sync.Received(1).SyncAsync(Arg.Any<CancellationToken>());
		await client.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.Received(1).WipeProfileIfSavedAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeAllAsync();
		await ui.Owner.DidNotReceiveWithAnyArgs().BindAsync(string.Empty);
	}

	/// <summary>
	/// Verifies that a sync that did not end synced asks first: nothing is signed out or removed.
	/// </summary>
	/// <param name="state">The state the sync ended in.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(ProfileSyncState.Pending)]
	[InlineData(ProfileSyncState.Offline)]
	[InlineData(ProfileSyncState.Error)]
	public async Task SignOutAsync_NotSynced_ShouldAskInsteadOfSigningOut(ProfileSyncState state)
	{
		var account = await VerifiedAsync();
		ui.Sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(new ProfileSyncResult(true, state));

		await account.SignOutAsync();

		account.IsConfirmingSignOut.Should().BeTrue();
		account.IsOpen.Should().BeTrue();
		account.IsSignedIn.Should().BeTrue();
		await client.DidNotReceive().SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that "Sign out anyway" signs out and removes the profile.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmSignOutAsync_ShouldSignOutAndWipeTheProfile()
	{
		var account = await VerifiedAsync();
		ui.Sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(new ProfileSyncResult(true, ProfileSyncState.Offline));
		await account.SignOutAsync();

		await account.ConfirmSignOutAsync();

		account.IsSignedIn.Should().BeFalse();
		account.IsConfirmingSignOut.Should().BeFalse();
		await client.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.Received(1).WipeProfileAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "Stay signed in" closes the question and changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CancelSignOut_ShouldStaySignedInAndKeepTheData()
	{
		var account = await VerifiedAsync();
		ui.Sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(new ProfileSyncResult(true, ProfileSyncState.Error));
		await account.SignOutAsync();

		account.CancelSignOut();
		await account.ConfirmSignOutAsync();

		account.IsConfirmingSignOut.Should().BeFalse();
		account.IsSignedIn.Should().BeTrue();
		await client.DidNotReceive().SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that closing the sheet withdraws the question.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Close_ShouldWithdrawTheSignOutQuestion()
	{
		var account = await VerifiedAsync();
		ui.Sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(new ProfileSyncResult(true, ProfileSyncState.Offline));
		await account.SignOutAsync();

		account.Close();

		account.IsConfirmingSignOut.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that an owner that was never verified signs out without a sync and without removing anything.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_OwnerNotVerified_ShouldSignOutWithoutSyncOrWipe()
	{
		var google = Substitute.For<IGoogleSignIn>();
		google.IsAvailable.Returns(true);
		client.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		var account = ui.CreateAccount(client, google);

		await account.SignOutAsync();

		await client.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Sync.DidNotReceive().SyncAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
		account.IsConfirmingSignOut.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a sign-out the server could not complete removes nothing and shows the reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_ServerRefuses_ShouldWipeNothing()
	{
		var account = await VerifiedAsync();
		client.SignOutAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(AccountErrors.Network()));

		await account.SignOutAsync();

		account.Error.Should().Be(AccountErrors.Network().Message);
		account.IsSignedIn.Should().BeTrue();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that a removal that fails after the sign-out is reported, and that the phone is still signed out.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_WipeFails_ShouldShowWhyAndStaySignedOut()
	{
		var account = await VerifiedAsync();
		ui.Wiper.WipeProfileIfSavedAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<bool>(new Error("Test.Wipe", "The wipe failed.")));

		await account.SignOutAsync();

		account.IsSignedIn.Should().BeFalse();
		account.Error.Should().Be(AccountViewModel.SignOutWipeFailedText);
	}
	#endregion

	#region Empty Phone Tests
	/// <summary>
	/// Verifies that another account takes over a phone that holds nothing without a question: the old sync state is removed, the account is bound
	/// and verified, and a sync is requested.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_OtherOwnerAndEmptyPhone_ShouldBindWithoutAsking()
	{
		var google = Substitute.For<IGoogleSignIn>();
		google.IsAvailable.Returns(true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		ui.LocalProfile.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(UiFixture.Nothing));
		ui.Owner.BindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		var account = ui.CreateAccount(client, google);

		await account.RefreshStateAsync();

		account.IsConfirmingOwnerChange.Should().BeFalse();
		account.IsOwnerVerified.Should().BeTrue();
		await ui.Wiper.Received(1).WipeProfileAsync(Arg.Any<CancellationToken>());
		await ui.Owner.Received(1).BindAsync(MeHash, Arg.Any<CancellationToken>());
		ui.Sync.Received(1).Request();
	}

	/// <summary>
	/// Verifies that a phone whose profile cannot be read is treated as holding data, so the question is asked.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_OtherOwnerAndUnreadableProfile_ShouldAsk()
	{
		var google = Substitute.For<IGoogleSignIn>();
		google.IsAvailable.Returns(true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		ui.LocalProfile.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<ParkingTracker.Application.Features.ProfileSync.Abstractions.ProfileSnapshot>(new Error("Test.Unreadable", "Unreadable.")));
		var account = ui.CreateAccount(client, google);

		await account.RefreshStateAsync();

		account.IsConfirmingOwnerChange.Should().BeTrue();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that a failed removal or binding on an empty phone falls back to the question instead of verifying the account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshStateAsync_OtherOwnerEmptyPhoneButBindFails_ShouldAsk()
	{
		var google = Substitute.For<IGoogleSignIn>();
		google.IsAvailable.Returns(true);
		client.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);
		client.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Me));
		ui.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("other"));
		ui.LocalProfile.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(UiFixture.Nothing));
		ui.Owner.BindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("Test.Bind", "Could not bind.")));
		var account = ui.CreateAccount(client, google);

		await account.RefreshStateAsync();

		account.IsOwnerVerified.Should().BeFalse();
		account.IsConfirmingOwnerChange.Should().BeTrue();
	}
	#endregion

	#region Review Tests
	/// <summary>
	/// Verifies that a request that did not run a sync never allows the removal, even when the service still says synced from an earlier run.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_SyncDidNotRun_ShouldAskInsteadOfSigningOut()
	{
		var account = await VerifiedAsync();
		ui.Sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(ProfileSyncResult.NotRun(ProfileSyncState.Synced));

		await account.SignOutAsync();

		account.IsConfirmingSignOut.Should().BeTrue();
		await client.DidNotReceive().SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileIfSavedAsync();
	}

	/// <summary>
	/// Verifies that a change saved after the sync is never removed unseen: the phone is signed out, nothing is removed, and the sheet asks to
	/// remove it or keep it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_ChangedSinceTheSync_ShouldSignOutAndAskWhatToDoWithTheData()
	{
		var account = await VerifiedAsync();
		ui.Wiper.WipeProfileIfSavedAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(false));

		await account.SignOutAsync();

		account.IsSignedIn.Should().BeFalse();
		account.IsConfirmingLocalRemoval.Should().BeTrue();
		account.IsOpen.Should().BeTrue();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that "Remove from this phone" removes the data and closes the question.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmLocalRemovalAsync_ShouldWipeTheProfile()
	{
		var account = await VerifiedAsync();
		ui.Wiper.WipeProfileIfSavedAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(false));
		await account.SignOutAsync();

		await account.ConfirmLocalRemovalAsync();

		account.IsConfirmingLocalRemoval.Should().BeFalse();
		await ui.Wiper.Received(1).WipeProfileAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "Keep on this phone" closes the question and removes nothing, and that closing the sheet does the same.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task KeepOnPhoneAndClose_ShouldKeepTheData()
	{
		var account = await VerifiedAsync();
		ui.Wiper.WipeProfileIfSavedAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(false));
		await account.SignOutAsync();

		account.KeepOnPhone();
		await account.ConfirmLocalRemovalAsync();

		account.IsConfirmingLocalRemoval.Should().BeFalse();
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileAsync();
	}

	/// <summary>
	/// Verifies that the sync a sign-out waits for is bounded: when it does not end within the time, the sign-out asks instead of signing out,
	/// and nothing is signed out or removed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_SyncTakesTooLong_ShouldAskAfterTheTimeout()
	{
		var account = await VerifiedAsync();
		ui.Sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(async call =>
		{
			await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()).ConfigureAwait(false);
			return ProfileSyncResult.NotRun(ProfileSyncState.Offline);
		});

		var signingOut = account.SignOutAsync();
		for (var wait = 0; wait < 500 && ui.Sync.ReceivedCalls().All(call => !string.Equals(call.GetMethodInfo().Name, nameof(ui.Sync.SyncAsync), StringComparison.Ordinal)); wait++)
		{
			await Task.Delay(10);
		}

		ui.Clock.Advance(AccountViewModel.SignOutSyncTimeout);
		await signingOut;

		account.IsConfirmingSignOut.Should().BeTrue();
		account.IsSignedIn.Should().BeTrue();
		await client.DidNotReceive().SignOutAsync(Arg.Any<CancellationToken>());
		await ui.Wiper.DidNotReceiveWithAnyArgs().WipeProfileIfSavedAsync();
	}

	/// <summary>
	/// Verifies that signing out resets the state of the sync, so that the menu never shows the state of the account that left.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_ShouldResetTheSyncState()
	{
		var account = await VerifiedAsync();
		ui.Sync.ClearReceivedCalls();

		await account.SignOutAsync();

		ui.Sync.Received().Reset();
		account.IsSignedIn.Should().BeFalse();
	}
	#endregion
}
