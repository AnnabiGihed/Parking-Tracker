using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.ProfileSync;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of <see cref="ProfileSyncService"/> over a scripted client and a scripted local store (ADR-0032): nothing is sent unless the
///              bound account is the verified one; the answers 304, 200 and 404 each give the right download, merge, upload or nothing; a stale
///              revision repeats from the read and stops after three rounds; a refusal stops at once; a change made during a round keeps the changed
///              flag and is followed by another round; a network failure ends offline and keeps the changes; concurrent callers share one run; and
///              the state and its event follow the run.
/// </summary>
public sealed class ProfileSyncServiceTests
{
	#region Test Infrastructure
	/// <summary>
	/// The hash of the verified account.
	/// </summary>
	private const string Hash = "hash-of-the-account";

	/// <summary>
	/// The moment of the older side.
	/// </summary>
	private static readonly DateTimeOffset Old = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// The moment of the newer side.
	/// </summary>
	private static readonly DateTimeOffset New = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Builds a place section.
	/// </summary>
	/// <param name="label">The label.</param>
	/// <param name="at">The moment.</param>
	/// <returns>The section.</returns>
	private static ProfilePlaceSection Place(string label, DateTimeOffset at) => new(label, 50.85, 4.35, "Rue", at);

	/// <summary>
	/// Builds a document with a home place, or none.
	/// </summary>
	/// <param name="home">The home section.</param>
	/// <returns>The document.</returns>
	private static ProfileDocument Document(ProfilePlaceSection? home = null)
	{
		return new ProfileDocument(ProfileDocument.SupportedSchemaVersion, home, null, new ProfileFavouritesSection(DateTimeOffset.UnixEpoch, []), null, null);
	}

	/// <summary>
	/// Builds a local snapshot.
	/// </summary>
	/// <param name="document">The local data.</param>
	/// <param name="baseRevision">The base revision, or <see langword="null"/>.</param>
	/// <param name="dirty">Whether the phone changed since.</param>
	/// <param name="home">The moment of the home place.</param>
	/// <returns>The snapshot.</returns>
	private static ProfileSnapshot Snapshot(ProfileDocument document, long? baseRevision = null, bool dirty = false, DateTimeOffset? home = null)
	{
		return new ProfileSnapshot(document, new SyncState(baseRevision, dirty, new SectionTimes(home, null, null, null, null)));
	}

	/// <summary>
	/// Builds a failed result of a call.
	/// </summary>
	/// <typeparam name="T">The type of the result.</typeparam>
	/// <param name="error">The error.</param>
	/// <returns>The failure.</returns>
	private static Result<T> Fail<T>(Error error) => Result.Failure<T>(error);

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Holds the service and its scripted collaborators.
	/// </summary>
	private sealed class Harness
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="Harness"/> class with an empty phone and an owner that is verified or not.
		/// </summary>
		/// <param name="verified">Whether the account is verified as the owner of the data.</param>
		public Harness(bool verified = true)
		{
			Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Hash));
			if (verified)
			{
				Verification.Verify(Hash);
			}

			Local.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Snapshot(Document())));
			Local.ApplyAsync(Arg.Any<ProfileSnapshot>(), Arg.Any<ProfileDocument>(), Arg.Any<SectionTimes>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(true));
			Local.MarkSavedAsync(Arg.Any<long?>(), Arg.Any<SectionTimes>(), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(true));
			Service = new ProfileSyncService(Client, Local, Owner, Verification, Screens, new FakeStartupGate(), NullLogger<ProfileSyncService>.Instance);
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets the scripted client of the API.
		/// </summary>
		public IProfileClient Client { get; } = Substitute.For<IProfileClient>();

		/// <summary>
		/// Gets the scripted local store.
		/// </summary>
		public IProfileLocalStore Local { get; } = Substitute.For<IProfileLocalStore>();

		/// <summary>
		/// Gets the scripted store of the bound account.
		/// </summary>
		public IDeviceOwnerStore Owner { get; } = Substitute.For<IDeviceOwnerStore>();

		/// <summary>
		/// Gets the record of the verified owner.
		/// </summary>
		public OwnerVerification Verification { get; } = new();

		/// <summary>
		/// Gets the scripted reloader of the screens.
		/// </summary>
		public IScreenReloader Screens { get; } = Substitute.For<IScreenReloader>();

		/// <summary>
		/// Gets the service under test.
		/// </summary>
		public ProfileSyncService Service { get; }
		#endregion

		#region Public Methods
		/// <summary>
		/// Makes the local store answer a snapshot.
		/// </summary>
		/// <param name="snapshot">The snapshot.</param>
		public void Reads(ProfileSnapshot snapshot) => Local.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(snapshot));

		/// <summary>
		/// Makes the client answer every read.
		/// </summary>
		/// <param name="answer">The answer.</param>
		public void Gets(Result<ProfileGetResult> answer) => Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(answer);

		/// <summary>
		/// Makes the client answer every write.
		/// </summary>
		/// <param name="answer">The answer.</param>
		public void Puts(Result<ProfilePutResult> answer) => Client.PutAsync(Arg.Any<ProfileDocument>(), Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(answer);
		#endregion
	}
	#endregion

	#region Owner Tests
	/// <summary>
	/// Verifies that without a verified owner nothing is read, sent or written and the state stays as it was.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_OwnerNotVerified_ShouldMakeNoCall()
	{
		var harness = new Harness(verified: false);

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Pending);
		await harness.Client.DidNotReceiveWithAnyArgs().GetAsync(null);
		await harness.Client.DidNotReceiveWithAnyArgs().PutAsync(null!, null);
		await harness.Local.DidNotReceiveWithAnyArgs().ReadAsync();
	}

	/// <summary>
	/// Verifies that a hash stored on the phone that differs from the verified one, or cannot be read, stops the sync before any call.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_BoundHashDiffersOrIsUnreadable_ShouldMakeNoCall()
	{
		var other = new Harness();
		other.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Success("another-account"));
		var unreadable = new Harness();
		unreadable.Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Fail<string>(DeviceDataErrors.NotStored));

		_ = await other.Service.SyncAsync();
		_ = await unreadable.Service.SyncAsync();

		await other.Client.DidNotReceiveWithAnyArgs().GetAsync(null);
		await unreadable.Client.DidNotReceiveWithAnyArgs().GetAsync(null);
	}
	#endregion

	#region Not Modified Tests
	/// <summary>
	/// Verifies that 304 on a phone that did not change is synced without an upload, and that the read carries the base revision.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NotModifiedAndNotDirty_ShouldBeSyncedWithoutUpload()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", Old)), baseRevision: 4, home: Old));
		harness.Gets(ProfileGetResult.NotModified);

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.Received(1).GetAsync(4, Arg.Any<CancellationToken>());
		await harness.Client.DidNotReceiveWithAnyArgs().PutAsync(null!, null);
	}

	/// <summary>
	/// Verifies that 304 on a phone that changed uploads the local document with the base revision and records the answer with the uploaded moments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NotModifiedAndDirty_ShouldUploadWithIfMatchAndMarkSaved()
	{
		var harness = new Harness();
		var snapshot = Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New);
		harness.Reads(snapshot);
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(ProfilePutResult.Saved(5));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.Received(1).PutAsync(snapshot.Document, 4, Arg.Any<CancellationToken>());
		await harness.Local.Received(1).MarkSavedAsync(5, snapshot.State.Times, Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Found Tests
	/// <summary>
	/// Verifies that the server copy is applied to an empty phone that never synced, without an upload and with the moments of the document.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_FirstSyncOnEmptyPhone_ShouldApplyTheServerCopy()
	{
		var harness = new Harness();
		var server = Document(Place("Server home", Old));
		harness.Gets(ProfileGetResult.Found(7, server));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Local.Received(1).ApplyAsync(Arg.Any<ProfileSnapshot>(), server, new SectionTimes(Old, null, null, null, null), 7, false, Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
		await harness.Client.DidNotReceiveWithAnyArgs().PutAsync(null!, null);
		await harness.Screens.Received(1).ReloadAsync();
	}

	/// <summary>
	/// Verifies that on a first sync with data on both sides the merge is applied locally and uploaded with the revision it was merged with.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_FirstSyncWithDataOnBothSides_ShouldMergeApplyAndUpload()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Local home", New)), home: New));
		harness.Gets(ProfileGetResult.Found(7, Document(Place("Server home", Old))));
		harness.Puts(ProfilePutResult.Saved(8));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Local.Received(1).ApplyAsync(Arg.Any<ProfileSnapshot>(), Arg.Is<ProfileDocument>(document => document.Home!.Label == "Local home"), Arg.Any<SectionTimes>(), 7, true, Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
		await harness.Client.Received(1).PutAsync(Arg.Is<ProfileDocument>(document => document.Home!.Label == "Local home"), 7, Arg.Any<CancellationToken>());
		await harness.Local.Received(1).MarkSavedAsync(8, new SectionTimes(New, null, null, null, null), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that when the server wins every section of a merge nothing is uploaded.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_MergeEqualsServerCopy_ShouldNotUpload()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Local home", Old)), dirty: true, home: Old));
		harness.Gets(ProfileGetResult.Found(7, Document(Place("Server home", New))));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Local.Received(1).ApplyAsync(Arg.Any<ProfileSnapshot>(), Arg.Any<ProfileDocument>(), Arg.Any<SectionTimes>(), 7, false, Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
		await harness.Client.DidNotReceiveWithAnyArgs().PutAsync(null!, null);
	}

	/// <summary>
	/// Verifies that a phone that already synced and did not change takes the server copy as it is, without a merge or an upload.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_BaseSetAndNotDirty_ShouldApplyTheServerCopy()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Old home", Old)), baseRevision: 4, home: Old));
		var server = Document();
		harness.Gets(ProfileGetResult.Found(6, server));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Local.Received(1).ApplyAsync(Arg.Any<ProfileSnapshot>(), server, SectionTimes.None, 6, false, Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
		await harness.Client.DidNotReceiveWithAnyArgs().PutAsync(null!, null);
	}

	/// <summary>
	/// Verifies that a phone that changed since its base merges, applies and uploads against the revision it just read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_BaseSetAndDirty_ShouldMergeAndUploadAgainstTheNewRevision()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Local home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.Found(6, Document(Place("Server home", Old))));
		harness.Puts(ProfilePutResult.Saved(7));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.Received(1).PutAsync(Arg.Is<ProfileDocument>(document => document.Home!.Label == "Local home"), 6, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the screens are not reloaded when the document changed no section of the phone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NoSectionDiffers_ShouldNotReloadTheScreens()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", Old)), baseRevision: 4, home: Old));
		harness.Gets(ProfileGetResult.Found(6, Document(Place("Home", Old))));

		_ = await harness.Service.SyncAsync();

		await harness.Screens.DidNotReceive().ReloadAsync();
	}

	/// <summary>
	/// Verifies that a screen that cannot be reloaded does not fail a sync whose data is written.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_ReloadThrows_ShouldStillBeSynced()
	{
		var harness = new Harness();
		harness.Screens.ReloadAsync().Throws(new InvalidOperationException("screen"));
		harness.Gets(ProfileGetResult.Found(7, Document(Place("Server home", Old))));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
	}
	#endregion

	#region Not Found Tests
	/// <summary>
	/// Verifies that an empty server with data on the phone is created with <c>If-None-Match: *</c>, which is a null revision.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NotFoundAndLocalData_ShouldCreateTheProfile()
	{
		var harness = new Harness();
		var snapshot = Snapshot(Document(Place("Home", New)), dirty: true, home: New);
		harness.Reads(snapshot);
		harness.Gets(ProfileGetResult.NotFound);
		harness.Puts(ProfilePutResult.Saved(1));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.Received(1).PutAsync(snapshot.Document, null, Arg.Any<CancellationToken>());
		await harness.Local.Received(1).MarkSavedAsync(1, snapshot.State.Times, Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that an empty server and an empty phone do nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NotFoundAndEmptyPhone_ShouldDoNothing()
	{
		var harness = new Harness();
		harness.Gets(ProfileGetResult.NotFound);

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.DidNotReceiveWithAnyArgs().PutAsync(null!, null);
		await harness.Local.DidNotReceiveWithAnyArgs().MarkSavedAsync(null, null!, null!);
	}

	/// <summary>
	/// Verifies that a phone the user emptied, with an empty server, clears its changed flag instead of waiting for an upload that never comes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NotFoundAndEmptiedPhone_ShouldClearTheFlagWithoutUpload()
	{
		var harness = new Harness();
		var snapshot = Snapshot(Document(), dirty: true, home: New);
		harness.Reads(snapshot);
		harness.Gets(ProfileGetResult.NotFound);

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.DidNotReceiveWithAnyArgs().PutAsync(null!, null);
		await harness.Local.Received(1).MarkSavedAsync(null, snapshot.State.Times, Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Stale And Refusal Tests
	/// <summary>
	/// Verifies that a stale revision repeats from the read, so that the next round merges what the other writer stored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_StaleOnce_ShouldReadAgainAndSucceed()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Client.PutAsync(Arg.Any<ProfileDocument>(), Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(Result.Success(ProfilePutResult.Stale), Result.Success(ProfilePutResult.Saved(6)));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.Received(2).GetAsync(4, Arg.Any<CancellationToken>());
		await harness.Client.Received(2).PutAsync(Arg.Any<ProfileDocument>(), 4, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a revision that stays stale stops after three rounds with an error, and keeps the changes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_StaleEveryTime_ShouldStopAfterThreeRounds()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(ProfilePutResult.Stale);

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Error);
		await harness.Client.Received(ProfileSyncService.MaximumRounds).GetAsync(4, Arg.Any<CancellationToken>());
		await harness.Client.Received(ProfileSyncService.MaximumRounds).PutAsync(Arg.Any<ProfileDocument>(), 4, Arg.Any<CancellationToken>());
		await harness.Local.DidNotReceiveWithAnyArgs().MarkSavedAsync(null, null!, null!);
	}

	/// <summary>
	/// Verifies that a refusal (400, 413, 428) stops at once, without a second upload, and ends in an error.
	/// </summary>
	/// <param name="status">The status of the refusal.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(400)]
	[InlineData(413)]
	[InlineData(428)]
	public async Task SyncAsync_Rejected_ShouldStopWithoutLooping(int status)
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(ProfilePutResult.Rejected(status));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Error);
		await harness.Client.Received(1).PutAsync(Arg.Any<ProfileDocument>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
		await harness.Local.DidNotReceiveWithAnyArgs().MarkSavedAsync(null, null!, null!);
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a network failure on the read ends offline and writes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NetworkFailureOnRead_ShouldBeOffline()
	{
		var harness = new Harness();
		harness.Gets(Fail<ProfileGetResult>(AccountErrors.Network()));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Offline);
		await harness.Local.DidNotReceiveWithAnyArgs().ApplyAsync(null!, null!, null!, null, false, null!);
	}

	/// <summary>
	/// Verifies that a network failure on the upload ends offline and keeps the changed flag, because nothing was recorded as saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NetworkFailureOnUpload_ShouldBeOfflineAndKeepTheFlag()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(Fail<ProfilePutResult>(AccountErrors.Network()));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Offline);
		await harness.Local.DidNotReceiveWithAnyArgs().MarkSavedAsync(null, null!, null!);
	}

	/// <summary>
	/// Verifies that a lost session, a server fault and a local read that fails end in an error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_SessionServerOrLocalFailure_ShouldBeAnError()
	{
		var session = new Harness();
		session.Gets(Fail<ProfileGetResult>(AccountErrors.NotSignedIn()));
		var server = new Harness();
		server.Gets(Fail<ProfileGetResult>(AccountErrors.Server()));
		var local = new Harness();
		local.Local.ReadAsync(Arg.Any<CancellationToken>()).Returns(Fail<ProfileSnapshot>(new Error("DeviceDatabase.Read.Failed", "The device database could not be read.")));

		(await session.Service.SyncAsync()).State.Should().Be(ProfileSyncState.Error);
		(await server.Service.SyncAsync()).State.Should().Be(ProfileSyncState.Error);
		(await local.Service.SyncAsync()).State.Should().Be(ProfileSyncState.Error);
	}

	/// <summary>
	/// Verifies that a write to the phone that fails ends in an error, and that an exception nobody expected does too.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_ApplyFailsOrThrows_ShouldBeAnError()
	{
		var fails = new Harness();
		fails.Gets(ProfileGetResult.Found(7, Document(Place("Server home", Old))));
		fails.Local.ApplyAsync(Arg.Any<ProfileSnapshot>(), Arg.Any<ProfileDocument>(), Arg.Any<SectionTimes>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>()).Returns(Fail<bool>(ProfileMapping.Invalid<bool>().Error));
		var throws = new Harness();
		throws.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("boom"));

		(await fails.Service.SyncAsync()).State.Should().Be(ProfileSyncState.Error);
		(await throws.Service.SyncAsync()).State.Should().Be(ProfileSyncState.Error);
	}
	#endregion

	#region Race Tests
	/// <summary>
	/// Verifies that an edit made between the snapshot and the write of a download stops that write, and the next round starts from the new snapshot.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_EditedDuringApply_ShouldRunAnotherRound()
	{
		var harness = new Harness();
		harness.Gets(ProfileGetResult.Found(7, Document(Place("Server home", Old))));
		harness.Local.ApplyAsync(Arg.Any<ProfileSnapshot>(), Arg.Any<ProfileDocument>(), Arg.Any<SectionTimes>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(false), Result.Success(true));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.Received(2).GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that an edit made during the upload keeps the changed flag, which the next round uploads.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_EditedDuringUpload_ShouldRunAnotherRoundThatUploadsTheEdit()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(ProfilePutResult.Saved(5));
		harness.Local.MarkSavedAsync(Arg.Any<long?>(), Arg.Any<SectionTimes>(), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(false), Result.Success(true));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Synced);
		await harness.Client.Received(2).PutAsync(Arg.Any<ProfileDocument>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that edits that keep racing the rounds end as pending, not as an error: the change signal starts the next sync.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_AlwaysEdited_ShouldEndPendingAfterTheRounds()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(ProfilePutResult.Saved(5));
		harness.Local.MarkSavedAsync(Arg.Any<long?>(), Arg.Any<SectionTimes>(), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(false));

		var state = (await harness.Service.SyncAsync()).State;

		state.Should().Be(ProfileSyncState.Pending);
		await harness.Client.Received(ProfileSyncService.MaximumRounds).GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Single Flight And State Tests
	/// <summary>
	/// Verifies that callers that arrive while a sync runs join it, get its result, and make it go through exactly one more pass when it ends:
	/// two reads of the server in all, never two at once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_ConcurrentCalls_ShouldShareOneRunAndGoThroughOneMorePass()
	{
		var harness = new Harness();
		var release = new TaskCompletionSource<Result<ProfileGetResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(release.Task);

		var first = harness.Service.SyncAsync();
		var second = harness.Service.SyncAsync();
		var third = harness.Service.SyncAsync();
		release.SetResult(ProfileGetResult.NotFound);
		var states = await Task.WhenAll(first, second, third);

		states.Should().OnlyContain(result => result.State == ProfileSyncState.Synced);
		await harness.Client.Received(2).GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a sync that ended frees the slot, so that the next call runs again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_AfterARun_ShouldRunAgain()
	{
		var harness = new Harness();
		harness.Gets(ProfileGetResult.NotFound);

		_ = await harness.Service.SyncAsync();
		_ = await harness.Service.SyncAsync();

		await harness.Client.Received(2).GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the state is syncing while the run waits for the server, and that the event follows each change of state.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task State_ShouldBeSyncingWhileRunningAndRaiseChanged()
	{
		var harness = new Harness();
		var release = new TaskCompletionSource<Result<ProfileGetResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var reading = new TaskCompletionSource();
		harness.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			reading.SetResult();
			return release.Task;
		});
		var seen = new List<ProfileSyncState>();
		harness.Service.Changed += (_, _) => seen.Add(harness.Service.State);

		var running = harness.Service.SyncAsync();
		await reading.Task;
		var during = harness.Service.State;
		release.SetResult(ProfileGetResult.NotFound);
		await running;

		harness.Service.State.Should().Be(ProfileSyncState.Synced);
		during.Should().Be(ProfileSyncState.Syncing);
		seen.Should().Equal(ProfileSyncState.Syncing, ProfileSyncState.Synced);
	}

	/// <summary>
	/// Verifies that a request starts a sync in the background.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Request_ShouldStartASync()
	{
		var harness = new Harness();
		var read = new TaskCompletionSource();
		harness.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			read.TrySetResult();
			return Result.Success(ProfileGetResult.NotFound);
		});

		harness.Service.Request();
		await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
		_ = await harness.Service.SyncAsync();

		harness.Service.State.Should().Be(ProfileSyncState.Synced);
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		var h = new Harness();
		var gate = new FakeStartupGate();
		var log = NullLogger<ProfileSyncService>.Instance;

		var noClient = () => new ProfileSyncService(null!, h.Local, h.Owner, h.Verification, h.Screens, gate, log); // null on purpose: proves the guard clause rejects it
		var noLocal = () => new ProfileSyncService(h.Client, null!, h.Owner, h.Verification, h.Screens, gate, log); // null on purpose: proves the guard clause rejects it
		var noOwner = () => new ProfileSyncService(h.Client, h.Local, null!, h.Verification, h.Screens, gate, log); // null on purpose: proves the guard clause rejects it
		var noVerification = () => new ProfileSyncService(h.Client, h.Local, h.Owner, null!, h.Screens, gate, log); // null on purpose: proves the guard clause rejects it
		var noScreens = () => new ProfileSyncService(h.Client, h.Local, h.Owner, h.Verification, null!, gate, log); // null on purpose: proves the guard clause rejects it
		var noGate = () => new ProfileSyncService(h.Client, h.Local, h.Owner, h.Verification, h.Screens, null!, log); // null on purpose: proves the guard clause rejects it
		var noLog = () => new ProfileSyncService(h.Client, h.Local, h.Owner, h.Verification, h.Screens, gate, null!); // null on purpose: proves the guard clause rejects it

		noClient.Should().Throw<ArgumentNullException>();
		noLocal.Should().Throw<ArgumentNullException>();
		noOwner.Should().Throw<ArgumentNullException>();
		noVerification.Should().Throw<ArgumentNullException>();
		noScreens.Should().Throw<ArgumentNullException>();
		noGate.Should().Throw<ArgumentNullException>();
		noLog.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Review Tests
	/// <summary>
	/// Verifies that a request that joins a sync whose pass was clean is not lost: the edit made after that pass read the phone is uploaded by the
	/// extra pass of the same run.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_RequestDuringACleanPass_ShouldUploadTheLaterEditInTheSameRun()
	{
		var harness = new Harness();
		var clean = Snapshot(Document(Place("Home", Old)), baseRevision: 4, home: Old);
		var edited = Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New);
		harness.Local.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(clean), Result.Success(clean), Result.Success(edited));
		var release = new TaskCompletionSource<Result<ProfileGetResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(release.Task, Task.FromResult(Result.Success(ProfileGetResult.NotModified)));
		harness.Puts(ProfilePutResult.Saved(5));

		var first = harness.Service.SyncAsync();
		var joined = harness.Service.SyncAsync();
		release.SetResult(ProfileGetResult.NotModified);
		var results = await Task.WhenAll(first, joined);

		results.Should().OnlyContain(result => result.IsSaved);
		await harness.Client.Received(1).PutAsync(Arg.Any<ProfileDocument>(), 4, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a round that wrote nothing reads the changed flag again before it reports synced, and runs another round when it is set.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_EditedAfterACleanSnapshot_ShouldNotReportSyncedWhileDirty()
	{
		var harness = new Harness();
		var clean = Snapshot(Document(Place("Home", Old)), baseRevision: 4, home: Old);
		var edited = Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New);
		harness.Local.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(clean), Result.Success(edited));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(ProfilePutResult.Saved(5));

		var result = await harness.Service.SyncAsync();

		result.IsSaved.Should().BeTrue();
		await harness.Client.Received(1).PutAsync(Arg.Any<ProfileDocument>(), 4, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a request that did not run a sync says so, even when an earlier run left the state at synced, so that nothing relies on it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_NotRun_ShouldNotCountAsSavedEvenAfterAnEarlierSyncedRun()
	{
		var harness = new Harness();
		harness.Gets(ProfileGetResult.NotFound);
		var ran = await harness.Service.SyncAsync();
		harness.Verification.Clear();

		var notRun = await harness.Service.SyncAsync();

		ran.Should().Be(new ProfileSyncResult(true, ProfileSyncState.Synced));
		ran.IsSaved.Should().BeTrue();
		notRun.Ran.Should().BeFalse();
		notRun.State.Should().Be(ProfileSyncState.Synced);
		notRun.IsSaved.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that every write to the phone names the account the sync was verified for.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_WritesToThePhone_ShouldNameTheVerifiedAccount()
	{
		var harness = new Harness();
		harness.Reads(Snapshot(Document(Place("Home", New)), baseRevision: 4, dirty: true, home: New));
		harness.Gets(ProfileGetResult.NotModified);
		harness.Puts(ProfilePutResult.Saved(5));
		var server = Document();
		var applied = new Harness();
		applied.Gets(ProfileGetResult.Found(7, Document(Place("Server home", Old))));

		_ = await harness.Service.SyncAsync();
		_ = await applied.Service.SyncAsync();

		await harness.Local.Received(1).MarkSavedAsync(5, Arg.Any<SectionTimes>(), Arg.Is<Func<string?>>(owner => owner() == Hash), Arg.Any<CancellationToken>());
		await applied.Local.Received(1).ApplyAsync(Arg.Any<ProfileSnapshot>(), Arg.Any<ProfileDocument>(), Arg.Any<SectionTimes>(), 7, false, Arg.Is<Func<string?>>(owner => owner() == Hash), Arg.Any<CancellationToken>());
		server.Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that when the account signs out while a round waits for the server, nothing is written to the phone afterwards and the run ends
	/// as pending without a second read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_OwnerClearedDuringTheRound_ShouldWriteNothing()
	{
		var harness = new Harness();
		var checkedAtTheWrite = new List<string?>();
		harness.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			harness.Verification.Clear();
			return Result.Success(ProfileGetResult.Found(7, Document(Place("Server home", Old))));
		});

		// The store of the phone asks for the verified account under its write gate, after the sign-out; it writes only when there is one.
		harness.Local.ApplyAsync(Arg.Any<ProfileSnapshot>(), Arg.Any<ProfileDocument>(), Arg.Any<SectionTimes>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<Func<string?>>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var verified = call.Arg<Func<string?>>()();
			checkedAtTheWrite.Add(verified);
			return Result.Success(verified is not null);
		});

		var result = await harness.Service.SyncAsync();

		result.State.Should().Be(ProfileSyncState.Pending);
		checkedAtTheWrite.Should().ContainSingle().Which.Should().BeNull();
		await harness.Screens.DidNotReceive().ReloadAsync();
		await harness.Client.Received(1).GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a reset puts the state back to pending and tells the listeners, and that a sync that ends after the account signed out does
	/// not bring its state back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Reset_ShouldForgetTheStateAndNotBeUndoneByARunThatEndsLater()
	{
		var harness = new Harness();
		harness.Gets(ProfileGetResult.NotFound);
		_ = await harness.Service.SyncAsync();
		var raised = 0;
		harness.Service.Changed += (_, _) => raised++;

		harness.Service.Reset();
		harness.Service.Reset();

		harness.Service.State.Should().Be(ProfileSyncState.Pending);
		raised.Should().Be(1);
		var release = new TaskCompletionSource<Result<ProfileGetResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(release.Task);
		var running = harness.Service.SyncAsync();
		harness.Verification.Clear();
		harness.Service.Reset();
		release.SetResult(ProfileGetResult.NotFound);
		await running;
		harness.Service.State.Should().Be(ProfileSyncState.Pending);
	}

	/// <summary>
	/// Verifies that a caller can stop waiting with a token while the sync goes on, and that the next caller gets the result of that sync.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SyncAsync_CancelledWait_ShouldThrowAndLeaveTheSyncRunning()
	{
		var harness = new Harness();
		var release = new TaskCompletionSource<Result<ProfileGetResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Client.GetAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(release.Task);
		using var source = new CancellationTokenSource();

		var waiting = harness.Service.SyncAsync(source.Token);
		await source.CancelAsync();
		var cancelled = async () => await waiting.ConfigureAwait(false);
		await cancelled.Should().ThrowAsync<OperationCanceledException>();
		var next = harness.Service.SyncAsync();
		release.SetResult(ProfileGetResult.NotFound);

		(await next).IsSaved.Should().BeTrue();
	}
	#endregion
}
