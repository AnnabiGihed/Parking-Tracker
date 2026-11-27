using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Services;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Testing.Device;
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Persistence.Device.Tests.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for the sync side of the device database against a real SQLite file (ADR-0032): the moment of a change is stamped with the
///              changed flag in the same scope as the data and is lost with it when the scope is not committed; the local profile reads back as a
///              document; applying a document writes only the sections that differ, never stamps them and refuses to overwrite a change made
///              since the snapshot; and recording an upload keeps the changed flag when something changed meanwhile.
/// </summary>
public sealed class ProfileLocalStoreTests
{
	#region Test Infrastructure
	/// <summary>
	/// The hash of the account the phone is bound to in these tests.
	/// </summary>
	private const string OwnerHash = "owner-hash";

	/// <summary>
	/// Creates the database of a test and binds the phone to <see cref="OwnerHash"/>.
	/// </summary>
	/// <returns>A task with the database.</returns>
	private static async Task<DeviceTestDatabase> CreateBoundAsync()
	{
		var db = await DeviceTestDatabase.CreateAsync().ConfigureAwait(false);
		(await db.Owner.BindAsync(OwnerHash).ConfigureAwait(false)).IsSuccess.Should().BeTrue();
		return db;
	}

	/// <summary>
	/// The moment the server sections were written.
	/// </summary>
	private static readonly DateTimeOffset ServerAt = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Builds a full document, as the server holds it.
	/// </summary>
	/// <param name="homeLabel">The label of the home, which tells documents apart.</param>
	/// <returns>The document.</returns>
	private static ProfileDocument FullDocument(string homeLabel = "Home")
	{
		return new ProfileDocument(
			ProfileDocument.SupportedSchemaVersion,
			new ProfilePlaceSection(homeLabel, 50.8467, 4.3525, "Grand-Place 1", ServerAt),
			new ProfilePlaceSection("Work", 50.8250, 4.3606, "Avenue Louise 1", ServerAt),
			new ProfileFavouritesSection(ServerAt, [new ProfileFavouritePlace("Gym", 50.84, 4.36, "Rue A"), new ProfileFavouritePlace("Club", 50.83, 4.37, string.Empty)]),
			new ProfileVehicleSection("tesla", "Model 3", 2023, "Electric", "Unknown", ["Type2", "Ccs"], 57.5, 491, "2XYZ987", ServerAt),
			new ProfileCardSection("bruxelles", "louise", new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), ["BXL_LIM_IXL_LOUISE"], [], [], "B012-345-678", "1ABC234", ServerAt));
	}

	/// <summary>
	/// Gives the moments a document carries.
	/// </summary>
	/// <param name="document">The document.</param>
	/// <returns>The moments.</returns>
	private static SectionTimes TimesOf(ProfileDocument document) => ProfileMerge.TimesOf(document);

	/// <summary>
	/// Saves a home place through the repository and stamps it, as a store does.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <param name="label">The label of the home.</param>
	/// <returns>A task that completes when the place is saved.</returns>
	private static async Task SaveHomeAsync(DeviceTestDatabase db, string label)
	{
		var list = PlaceList.Create();
		_ = list.SetHome(label, new GeoPoint(50.85, 4.35), "Rue de la Loi");
		var saved = await db.ApplyAsync(async scope =>
		{
			var staged = await db.Places.UpdateAsync(scope, list).ConfigureAwait(false);
			db.Tracker.Stage(scope, SyncSection.Home);
			return staged;
		}).ConfigureAwait(false);
		saved.IsSuccess.Should().BeTrue();
	}
	#endregion

	#region Stamp Tests
	/// <summary>
	/// Verifies that a phone with no sync state reads as the empty state.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_NoRow_ShouldBeTheEmptyState()
	{
		await using var db = await CreateBoundAsync();

		var state = await db.SyncState.GetAsync();

		state.Value.Should().Be(SyncState.Empty);
	}

	/// <summary>
	/// Verifies that a staged stamp creates the row with the moment of the clock to the millisecond and the changed flag, and that a second
	/// stamp of another section keeps the first.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StageTouch_ShouldStampTheSectionAndSetTheFlagWhenCommitted()
	{
		await using var db = await CreateBoundAsync();

		await SaveHomeAsync(db, "Home");
		db.Clock.Advance(TimeSpan.FromMinutes(1));
		var second = await db.ApplyRemovalAsync(scope => db.Tracker.Stage(scope, SyncSection.Card));
		var state = (await db.SyncState.GetAsync()).Value;

		second.IsSuccess.Should().BeTrue();
		state.IsDirty.Should().BeTrue();
		state.BaseRevision.Should().BeNull();
		state.Times.Home.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
		state.Times.Card.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 1, 0, TimeSpan.Zero));
		state.Times.Work.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a stamp staged in a scope that is disposed without a commit leaves nothing behind, so that data and stamp go together.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StageTouch_ScopeNotCommitted_ShouldLeaveNoStamp()
	{
		await using var db = await CreateBoundAsync();
		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			db.Tracker.Stage(scope, SyncSection.Home);
		}

		var state = (await db.SyncState.GetAsync()).Value;

		state.Should().Be(SyncState.Empty);
	}

	/// <summary>
	/// Verifies that the tracker tells its listeners once a store reports a commit, and not before.
	/// </summary>
	[Fact]
	public void NotifyCommitted_ShouldRaiseChanged()
	{
		var db = DeviceTestDatabase.CreateEmpty();
		var raised = 0;
		db.Tracker.Changed += (_, _) => raised++;

		db.Tracker.NotifyCommitted();

		raised.Should().Be(1);
	}

	/// <summary>
	/// Verifies that storing the whole state keeps the flag of a pending removal, which belongs to the account flow.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StageState_ShouldKeepTheErasePendingFlag()
	{
		await using var db = await CreateBoundAsync();
		await db.ExecuteAsync("INSERT INTO sync_state (id, erase_pending) VALUES (1, 1)");

		var written = await db.ApplyRemovalAsync(scope => db.SyncState.StageState(scope, new SyncState(7, true, SectionTimes.None)));

		written.IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT erase_pending FROM sync_state")).Should().Be(1);
		(await db.ScalarAsync<long>("SELECT base_revision FROM sync_state")).Should().Be(7);
	}
	#endregion

	#region Read Tests
	/// <summary>
	/// Verifies that an empty phone reads as an empty profile with the empty state.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_EmptyPhone_ShouldBeEmpty()
	{
		await using var db = await CreateBoundAsync();

		var read = await db.LocalProfile.ReadAsync();

		read.Value.IsEmpty.Should().BeTrue();
		read.Value.State.Should().Be(SyncState.Empty);
	}

	/// <summary>
	/// Verifies that saved data reads back as sections that carry the moment of their stamp, and the epoch when they have none.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_SavedHomeAndCard_ShouldCarryStampsOrTheEpoch()
	{
		await using var db = await CreateBoundAsync();
		await SaveHomeAsync(db, "Home");
		var card = TestData.CityCard();
		(await db.ApplyAsync(scope => db.Cards.UpdateAsync(scope, card))).IsSuccess.Should().BeTrue();

		var read = (await db.LocalProfile.ReadAsync()).Value;

		read.IsEmpty.Should().BeFalse();
		read.Document.Home!.UpdatedAt.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
		read.Document.Card!.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch);
		read.Document.Card.Number.Should().Be("B012-345-678");
		read.Document.Card.Plate.Should().Be("1ABC234");
		read.Document.Work.Should().BeNull();
		read.Document.Vehicle.Should().BeNull();
	}
	#endregion

	#region Cleared Tests
	/// <summary>
	/// Verifies that a section the user cleared reads back absent with its moment in the cleared sections of the document, and that a section
	/// that never changed has no entry.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_ClearedHome_ShouldCarryTheMomentOfTheClear()
	{
		await using var db = await CreateBoundAsync();
		await SaveHomeAsync(db, "Home");
		db.Clock.Advance(TimeSpan.FromMinutes(5));
		(await db.ApplyRemovalAsync(scope =>
		{
			db.Places.Remove(scope);
			db.Tracker.Stage(scope, SyncSection.Home);
		})).IsSuccess.Should().BeTrue();

		var read = (await db.LocalProfile.ReadAsync()).Value;

		read.Document.Home.Should().BeNull();
		read.Document.Cleared.Should().Be(new ProfileClearedSections(new DateTimeOffset(2026, 11, 18, 9, 5, 0, TimeSpan.Zero), null, null, null));
		read.IsEmpty.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a document with a clear removes the section from the phone and stores the moment of the clear, which reads back unchanged.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_ClearedSection_ShouldRemoveItAndKeepTheMoment()
	{
		await using var db = await CreateBoundAsync();
		var first = (await db.LocalProfile.ReadAsync()).Value;
		_ = await db.LocalProfile.ApplyAsync(first, FullDocument(), TimesOf(FullDocument()), 1, needsUpload: false, () => OwnerHash);
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		var cleared = FullDocument() with { Home = null, Cleared = new ProfileClearedSections(ServerAt.AddHours(1), null, null, null) };

		var applied = await db.LocalProfile.ApplyAsync(snapshot, cleared, TimesOf(cleared), 2, needsUpload: false, () => OwnerHash);
		var read = (await db.LocalProfile.ReadAsync()).Value;

		applied.Value.Should().BeTrue();
		read.Document.Home.Should().BeNull();
		read.State.Times.Home.Should().Be(ServerAt.AddHours(1));
		ProfileMerge.AreSame(read.Document, cleared).Should().BeTrue();
	}
	#endregion

	#region Apply Tests
	/// <summary>
	/// Verifies that a document from the server is written to an empty phone for every section, reads back equal, stores the revision and moments,
	/// and does not mark the phone as changed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_ServerDocument_ShouldFillThePhoneWithoutMarkingItChanged()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		var document = FullDocument();

		var applied = await db.LocalProfile.ApplyAsync(snapshot, document, TimesOf(document), 3, needsUpload: false, () => OwnerHash);
		var read = (await db.LocalProfile.ReadAsync()).Value;

		applied.IsSuccess.Should().BeTrue("{0}", applied.IsFailure ? applied.Error.Code + applied.Error.Message : string.Empty);
		ProfileMerge.AreSame(read.Document, document).Should().BeTrue();
		read.State.BaseRevision.Should().Be(3);
		read.State.IsDirty.Should().BeFalse();
		read.State.Times.Should().Be(TimesOf(document));
	}

	/// <summary>
	/// Verifies that a document that still has to be uploaded keeps the changed flag.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_NeedsUpload_ShouldKeepTheFlag()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;

		_ = await db.LocalProfile.ApplyAsync(snapshot, FullDocument(), TimesOf(FullDocument()), 3, needsUpload: true, () => OwnerHash);

		(await db.SyncState.GetAsync()).Value.IsDirty.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a section that is absent from the document is removed from the phone, and that sections that did not change keep their rows.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_SectionAbsent_ShouldRemoveItAndKeepTheOthers()
	{
		await using var db = await CreateBoundAsync();
		var first = (await db.LocalProfile.ReadAsync()).Value;
		_ = await db.LocalProfile.ApplyAsync(first, FullDocument(), TimesOf(FullDocument()), 1, needsUpload: false, () => OwnerHash);
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		var withoutHomeAndCard = FullDocument() with { Home = null, Card = null };
		var identifiersBefore = (await db.Places.GetAsync()).Value.Places.Where(place => place.Kind == ParkingTracker.Domain.Features.SavedPlaces.ValueObjects.PlaceKind.Favourite).Select(place => place.Id).ToArray();

		var applied = await db.LocalProfile.ApplyAsync(snapshot, withoutHomeAndCard, TimesOf(withoutHomeAndCard), 2, needsUpload: false, () => OwnerHash);
		var read = (await db.LocalProfile.ReadAsync()).Value;

		applied.Value.Should().BeTrue();
		read.Document.Home.Should().BeNull();
		read.Document.Card.Should().BeNull();
		read.Document.Work.Should().NotBeNull();
		read.Document.Vehicle.Should().NotBeNull();
		(await db.Places.GetAsync()).Value.Places.Where(place => place.Kind == ParkingTracker.Domain.Features.SavedPlaces.ValueObjects.PlaceKind.Favourite).Select(place => place.Id).Should().Equal(identifiersBefore);
	}

	/// <summary>
	/// Verifies that an empty document empties the phone, including the list of places.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_EmptyDocument_ShouldEmptyThePhone()
	{
		await using var db = await CreateBoundAsync();
		var first = (await db.LocalProfile.ReadAsync()).Value;
		_ = await db.LocalProfile.ApplyAsync(first, FullDocument(), TimesOf(FullDocument()), 1, needsUpload: false, () => OwnerHash);
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		var empty = new ProfileDocument(ProfileDocument.SupportedSchemaVersion, null, null, new ProfileFavouritesSection(ServerAt, []), null, null);

		var applied = await db.LocalProfile.ApplyAsync(snapshot, empty, TimesOf(empty), 2, needsUpload: false, () => OwnerHash);

		applied.Value.Should().BeTrue();
		(await db.LocalProfile.ReadAsync()).Value.IsEmpty.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that an edit made after the snapshot (a new stamp) stops the write: nothing is overwritten and the flag of the edit stays.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_EditedSinceTheSnapshot_ShouldWriteNothing()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		await SaveHomeAsync(db, "Edited meanwhile");

		var applied = await db.LocalProfile.ApplyAsync(snapshot, FullDocument(), TimesOf(FullDocument()), 3, needsUpload: false, () => OwnerHash);
		var read = (await db.LocalProfile.ReadAsync()).Value;

		applied.Value.Should().BeFalse();
		read.Document.Home!.Label.Should().Be("Edited meanwhile");
		read.Document.Work.Should().BeNull();
		read.State.IsDirty.Should().BeTrue();
		read.State.BaseRevision.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a section the phone cannot hold (an unknown fuel) fails the write as a whole and changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_SectionCannotBeHeld_ShouldFailAndChangeNothing()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		var broken = FullDocument() with { Vehicle = FullDocument().Vehicle! with { Fuel = "Steam" } };

		var applied = await db.LocalProfile.ApplyAsync(snapshot, broken, TimesOf(broken), 3, needsUpload: false, () => OwnerHash);

		applied.IsFailure.Should().BeTrue();
		applied.Error.Code.Should().Be(ProfileMapping.InvalidSectionCode);
		(await db.LocalProfile.ReadAsync()).Value.IsEmpty.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a favourite outside Brussels fails the write as a whole.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_PlaceOutsideBrussels_ShouldFail()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		var broken = FullDocument() with { Home = new ProfilePlaceSection("Home", 48.85, 2.35, null, ServerAt) };

		var applied = await db.LocalProfile.ApplyAsync(snapshot, broken, TimesOf(broken), 3, needsUpload: false, () => OwnerHash);

		applied.IsFailure.Should().BeTrue();
		(await db.LocalProfile.ReadAsync()).Value.IsEmpty.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_NullArguments_ShouldThrowArgumentNullException()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;

		var noSnapshot = () => db.LocalProfile.ApplyAsync(null!, FullDocument(), SectionTimes.None, 1, false, () => OwnerHash); // null on purpose: proves the guard clause rejects it
		var noDocument = () => db.LocalProfile.ApplyAsync(snapshot, null!, SectionTimes.None, 1, false, () => OwnerHash); // null on purpose: proves the guard clause rejects it
		var noTimes = () => db.LocalProfile.ApplyAsync(snapshot, FullDocument(), null!, 1, false, () => OwnerHash); // null on purpose: proves the guard clause rejects it

		await noSnapshot.Should().ThrowAsync<ArgumentNullException>();
		await noDocument.Should().ThrowAsync<ArgumentNullException>();
		await noTimes.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion

	#region Mark Saved Tests
	/// <summary>
	/// Verifies that an upload of the current moments stores the revision and clears the flag.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MarkSavedAsync_NothingChanged_ShouldClearTheFlag()
	{
		await using var db = await CreateBoundAsync();
		await SaveHomeAsync(db, "Home");
		var uploaded = (await db.SyncState.GetAsync()).Value.Times;

		var marked = await db.LocalProfile.MarkSavedAsync(5, uploaded, () => OwnerHash);
		var state = (await db.SyncState.GetAsync()).Value;

		marked.Value.Should().BeTrue();
		state.IsDirty.Should().BeFalse();
		state.BaseRevision.Should().Be(5);
		state.Times.Should().Be(uploaded);
	}

	/// <summary>
	/// Verifies that an edit made during the upload keeps the flag while the new revision is stored, so that another round follows.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MarkSavedAsync_EditedDuringTheUpload_ShouldKeepTheFlag()
	{
		await using var db = await CreateBoundAsync();
		await SaveHomeAsync(db, "Home");
		var uploaded = (await db.SyncState.GetAsync()).Value.Times;
		db.Clock.Advance(TimeSpan.FromSeconds(5));
		await SaveHomeAsync(db, "Edited during the upload");

		var marked = await db.LocalProfile.MarkSavedAsync(5, uploaded, () => OwnerHash);
		var state = (await db.SyncState.GetAsync()).Value;

		marked.Value.Should().BeFalse();
		state.IsDirty.Should().BeTrue();
		state.BaseRevision.Should().Be(5);
	}

	/// <summary>
	/// Verifies that a missing argument is rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MarkSavedAsync_NoMoments_ShouldThrowArgumentNullException()
	{
		await using var db = await CreateBoundAsync();

		var noTimes = () => db.LocalProfile.MarkSavedAsync(1, null!, () => OwnerHash); // null on purpose: proves the guard clause rejects it

		await noTimes.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion

	#region Owner And Removal Guard Tests
	/// <summary>
	/// Verifies that the verified account is asked for under the write gate: a sign-out that lands after the hash was read, but before the write,
	/// leaves the phone as it was, with the bound owner still the same.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_SignedOutBeforeTheWrite_ShouldWriteNothing()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		string? verified = OwnerHash;
		var hashRead = verified;
		verified = null;

		var applied = await db.LocalProfile.ApplyAsync(snapshot, FullDocument(), TimesOf(FullDocument()), 3, needsUpload: false, () => verified);
		var marked = await db.LocalProfile.MarkSavedAsync(3, snapshot.State.Times, () => verified);

		hashRead.Should().Be(OwnerHash);
		applied.Value.Should().BeFalse();
		marked.Value.Should().BeFalse();
		(await db.LocalProfile.ReadAsync()).Value.IsEmpty.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sync_state")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that nothing is applied when the phone is bound to another account, or to none, whatever the snapshot says.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_BoundToAnotherAccountOrNone_ShouldWriteNothing()
	{
		await using var db = await CreateBoundAsync();
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		(await db.Owner.BindAsync("another-account")).IsSuccess.Should().BeTrue();

		var other = await db.LocalProfile.ApplyAsync(snapshot, FullDocument(), TimesOf(FullDocument()), 3, needsUpload: false, () => OwnerHash);
		await db.ExecuteAsync("DELETE FROM device_owner");
		var none = await db.LocalProfile.ApplyAsync(snapshot, FullDocument(), TimesOf(FullDocument()), 3, needsUpload: false, () => OwnerHash);

		other.Value.Should().BeFalse();
		none.Value.Should().BeFalse();
		(await db.LocalProfile.ReadAsync()).Value.IsEmpty.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a profile that was removed since the snapshot (the sync state is gone) is not written to again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApplyAsync_ProfileRemovedSinceTheSnapshot_ShouldWriteNothing()
	{
		await using var db = await CreateBoundAsync();
		var first = (await db.LocalProfile.ReadAsync()).Value;
		_ = await db.LocalProfile.ApplyAsync(first, FullDocument(), TimesOf(FullDocument()), 3, needsUpload: false, () => OwnerHash);
		var snapshot = (await db.LocalProfile.ReadAsync()).Value;
		await db.ExecuteAsync("DELETE FROM sync_state");
		await db.ExecuteAsync("DELETE FROM saved_places");

		var applied = await db.LocalProfile.ApplyAsync(snapshot, FullDocument(homeLabel: "Again"), TimesOf(FullDocument()), 4, needsUpload: false, () => OwnerHash);

		applied.Value.Should().BeFalse();
		(await db.SyncState.GetAsync()).Value.Should().Be(SyncState.Empty);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that recording an upload does nothing for another account, and does not bring back the state of a profile that was removed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MarkSavedAsync_OtherAccountOrRemovedProfile_ShouldWriteNothing()
	{
		await using var db = await CreateBoundAsync();
		await SaveHomeAsync(db, "Home");
		var uploaded = (await db.SyncState.GetAsync()).Value.Times;

		var other = await db.LocalProfile.MarkSavedAsync(5, uploaded, () => "another-account");
		await db.ExecuteAsync("DELETE FROM sync_state");
		var removed = await db.LocalProfile.MarkSavedAsync(5, uploaded, () => OwnerHash);

		other.Value.Should().BeFalse();
		removed.Value.Should().BeFalse();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sync_state")).Should().Be(0);
	}
	#endregion
}
