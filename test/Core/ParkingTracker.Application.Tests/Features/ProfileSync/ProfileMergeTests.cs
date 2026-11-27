using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Services;
using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Application.Tests.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ProfileMerge"/>, the merge rules of the profile sync (ADR-0032): for each section the newer moment wins,
///              a missing moment (none, or the epoch) is older than any moment, a tie goes to the server, a section cleared on the phone beats an
///              older server value, data wins over an empty section when neither side has a moment, the favourites are one section, and two
///              documents are compared by value.
/// </summary>
public sealed class ProfileMergeTests
{
	#region Test Infrastructure
	/// <summary>
	/// The moment the older side changed.
	/// </summary>
	private static readonly DateTimeOffset Old = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// The moment the newer side changed.
	/// </summary>
	private static readonly DateTimeOffset New = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Builds a place section.
	/// </summary>
	/// <param name="label">The label, which tells the sides apart.</param>
	/// <param name="at">The moment.</param>
	/// <returns>The section.</returns>
	private static ProfilePlaceSection Place(string label, DateTimeOffset at) => new(label, 50.85, 4.35, "Rue de la Loi", at);

	/// <summary>
	/// Builds a vehicle section.
	/// </summary>
	/// <param name="make">The make, which tells the sides apart.</param>
	/// <param name="at">The moment.</param>
	/// <returns>The section.</returns>
	private static ProfileVehicleSection Vehicle(string make, DateTimeOffset at) => new(make, "Model", 2020, "Petrol", "Euro6", [], null, null, "1ABC123", at);

	/// <summary>
	/// Builds a card section.
	/// </summary>
	/// <param name="number">The number, which tells the sides apart.</param>
	/// <param name="at">The moment.</param>
	/// <returns>The section.</returns>
	private static ProfileCardSection Card(string number, DateTimeOffset at) => new("brussels", "A", null, null, [], [], [], number, "1ABC123", at);

	/// <summary>
	/// Builds a favourites section.
	/// </summary>
	/// <param name="at">The moment.</param>
	/// <param name="labels">The labels of the places.</param>
	/// <returns>The section.</returns>
	private static ProfileFavouritesSection Favourites(DateTimeOffset at, params string[] labels)
	{
		return new ProfileFavouritesSection(at, labels.Select(label => new ProfileFavouritePlace(label, 50.85, 4.35, null)).ToArray());
	}

	/// <summary>
	/// Builds a document.
	/// </summary>
	/// <param name="home">The home section.</param>
	/// <param name="work">The work section.</param>
	/// <param name="favourites">The favourites section.</param>
	/// <param name="vehicle">The vehicle section.</param>
	/// <param name="card">The card section.</param>
	/// <returns>The document.</returns>
	private static ProfileDocument Document(ProfilePlaceSection? home = null, ProfilePlaceSection? work = null, ProfileFavouritesSection? favourites = null, ProfileVehicleSection? vehicle = null, ProfileCardSection? card = null)
	{
		return new ProfileDocument(ProfileDocument.SupportedSchemaVersion, home, work, favourites ?? Favourites(DateTimeOffset.UnixEpoch), vehicle, card);
	}

	/// <summary>
	/// Builds a local snapshot from a document and the moments of its sections.
	/// </summary>
	/// <param name="document">The local data.</param>
	/// <param name="times">The moments, or <see langword="null"/> for none.</param>
	/// <returns>The snapshot.</returns>
	private static ProfileSnapshot Local(ProfileDocument document, SectionTimes? times = null) => new(document, new SyncState(null, true, times ?? SectionTimes.None));
	#endregion

	#region Home And Work Tests
	/// <summary>
	/// Verifies that the newer home wins, from either side.
	/// </summary>
	[Fact]
	public void Merge_Home_ShouldTakeTheNewerSide()
	{
		var localNewer = ProfileMerge.Merge(Local(Document(home: Place("local", New)), new SectionTimes(New, null, null, null, null)), Document(home: Place("server", Old)));
		var serverNewer = ProfileMerge.Merge(Local(Document(home: Place("local", Old)), new SectionTimes(Old, null, null, null, null)), Document(home: Place("server", New)));

		localNewer.Document.Home!.Label.Should().Be("local");
		localNewer.Times.Home.Should().Be(New);
		serverNewer.Document.Home!.Label.Should().Be("server");
		serverNewer.Times.Home.Should().Be(New);
	}

	/// <summary>
	/// Verifies that the work place is merged on its own, apart from the home.
	/// </summary>
	[Fact]
	public void Merge_HomeAndWork_ShouldBeDecidedSeparately()
	{
		var local = Local(Document(Place("local home", New), Place("local work", Old)), new SectionTimes(New, Old, null, null, null));

		var merged = ProfileMerge.Merge(local, Document(Place("server home", Old), Place("server work", New)));

		merged.Document.Home!.Label.Should().Be("local home");
		merged.Document.Work!.Label.Should().Be("server work");
	}

	/// <summary>
	/// Verifies that on equal moments the server wins.
	/// </summary>
	[Fact]
	public void Merge_EqualMoments_ShouldTakeTheServer()
	{
		var local = Local(Document(home: Place("local", New)), new SectionTimes(New, null, null, null, null));

		var merged = ProfileMerge.Merge(local, Document(home: Place("server", New)));

		merged.Document.Home!.Label.Should().Be("server");
	}

	/// <summary>
	/// Verifies that moments equal to the millisecond count as equal, whatever their offsets.
	/// </summary>
	[Fact]
	public void Merge_SameInstantInAnotherOffset_ShouldTakeTheServer()
	{
		var local = Local(Document(home: Place("local", New)), new SectionTimes(New, null, null, null, null));

		var merged = ProfileMerge.Merge(local, Document(home: Place("server", New.ToOffset(TimeSpan.FromHours(2)))));

		merged.Document.Home!.Label.Should().Be("server");
	}

	/// <summary>
	/// Verifies that a section without a moment on the phone loses to a server section that has one.
	/// </summary>
	[Fact]
	public void Merge_LocalWithoutMoment_ShouldLoseToAnyServerMoment()
	{
		var merged = ProfileMerge.Merge(Local(Document(home: Place("local", DateTimeOffset.UnixEpoch))), Document(home: Place("server", Old)));

		merged.Document.Home!.Label.Should().Be("server");
	}

	/// <summary>
	/// Verifies that a server section with the epoch as its moment loses to a section the phone changed.
	/// </summary>
	[Fact]
	public void Merge_ServerWithEpoch_ShouldLoseToAnyLocalMoment()
	{
		var local = Local(Document(home: Place("local", Old)), new SectionTimes(Old, null, null, null, null));

		var merged = ProfileMerge.Merge(local, Document(home: Place("server", DateTimeOffset.UnixEpoch)));

		merged.Document.Home!.Label.Should().Be("local");
	}

	/// <summary>
	/// Verifies that a home the user cleared (absent, with a newer moment) beats an older server home, and keeps its moment.
	/// </summary>
	[Fact]
	public void Merge_ClearedLocallyAndNewer_ShouldRemoveTheServerSection()
	{
		var local = Local(Document(), new SectionTimes(New, null, null, null, null));

		var merged = ProfileMerge.Merge(local, Document(home: Place("server", Old)));

		merged.Document.Home.Should().BeNull();
		merged.Times.Home.Should().Be(New);
	}

	/// <summary>
	/// Verifies that the merge of a clear carries the moment of the clear in the document, so that the other phone can compare it.
	/// </summary>
	[Fact]
	public void Merge_ClearedLocally_ShouldCarryTheMomentInTheDocument()
	{
		var local = Local(Document(card: Card("111", Old)), new SectionTimes(New, null, null, null, Old));

		var merged = ProfileMerge.Merge(local, Document(home: Place("server", Old)));

		merged.Document.Cleared.Should().Be(new ProfileClearedSections(New, null, null, null));
	}

	/// <summary>
	/// Verifies the two-phone case: phone A cleared the home at the newer moment and uploaded it; phone B, offline, still holds the home from
	/// the older moment and merges. The clear wins on B, which loses the home and keeps the moment.
	/// </summary>
	[Fact]
	public void Merge_ServerClearedAfterTheLocalHome_ShouldKeepTheClear()
	{
		var local = Local(Document(home: Place("home of phone B", Old)), new SectionTimes(Old, null, null, null, null));
		var server = Document() with { Cleared = new ProfileClearedSections(New, null, null, null) };

		var merged = ProfileMerge.Merge(local, server);

		merged.Document.Home.Should().BeNull();
		merged.Times.Home.Should().Be(New);
		merged.Document.Cleared!.Home.Should().Be(New);
		ProfileMerge.AreSame(merged.Document, server).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a clear of the server older than a home edit of the phone loses, in the document too.
	/// </summary>
	[Fact]
	public void Merge_ServerClearOlderThanTheLocalHome_ShouldKeepTheLocalHome()
	{
		var local = Local(Document(home: Place("local", New)), new SectionTimes(New, null, null, null, null));
		var server = Document() with { Cleared = new ProfileClearedSections(Old, null, null, null) };

		var merged = ProfileMerge.Merge(local, server);

		merged.Document.Home!.Label.Should().Be("local");
		merged.Document.Cleared.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a local clear older than the value on the server loses, and that the other three sections follow the same rule.
	/// </summary>
	[Fact]
	public void Merge_LocalClearOlderThanTheServerValue_ShouldTakeTheServerValue()
	{
		var local = Local(Document(), new SectionTimes(Old, Old, null, Old, Old));

		var merged = ProfileMerge.Merge(local, Document(Place("home", New), Place("work", New), null, Vehicle("make", New), Card("111", New)));

		merged.Document.Home!.Label.Should().Be("home");
		merged.Document.Work!.Label.Should().Be("work");
		merged.Document.Vehicle!.Make.Should().Be("make");
		merged.Document.Card!.Number.Should().Be("111");
		merged.Document.Cleared.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a clear of the server is read as the moment of the absent section, for the work place, the vehicle and the card too.
	/// </summary>
	[Fact]
	public void TimesOf_ClearedSections_ShouldBeTheMomentOfTheAbsentSections()
	{
		var document = Document() with { Cleared = new ProfileClearedSections(null, New, Old, DateTimeOffset.UnixEpoch) };

		ProfileMerge.TimesOf(document).Should().Be(new SectionTimes(null, New, null, Old, null));
	}

	/// <summary>
	/// Verifies that documents differ when the moment of a clear differs, and that no entry equals an entry without moments.
	/// </summary>
	[Fact]
	public void AreSame_ClearedDiffers_ShouldBeFalseUnlessBothAreEmpty()
	{
		var plain = Document();

		ProfileMerge.AreSame(plain, plain with { Cleared = new ProfileClearedSections(New, null, null, null) }).Should().BeFalse();
		ProfileMerge.AreSame(plain with { Cleared = new ProfileClearedSections(Old, null, null, null) }, plain with { Cleared = new ProfileClearedSections(New, null, null, null) }).Should().BeFalse();
		ProfileMerge.AreSame(plain, plain with { Cleared = new ProfileClearedSections(null, null, null, null) }).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a home cleared before the server's newer one does not remove it.
	/// </summary>
	[Fact]
	public void Merge_ClearedLocallyButOlder_ShouldKeepTheServerSection()
	{
		var local = Local(Document(), new SectionTimes(Old, null, null, null, null));

		var merged = ProfileMerge.Merge(local, Document(home: Place("server", New)));

		merged.Document.Home!.Label.Should().Be("server");
	}

	/// <summary>
	/// Verifies that data from before the sync existed is kept when the server holds nothing for the section and neither side has a moment.
	/// </summary>
	[Fact]
	public void Merge_NoMomentOnEitherSide_ShouldKeepLocalDataOverAnEmptyServerSection()
	{
		var merged = ProfileMerge.Merge(Local(Document(home: Place("local", DateTimeOffset.UnixEpoch))), Document());

		merged.Document.Home!.Label.Should().Be("local");
		merged.Times.Home.Should().BeNull();
	}

	/// <summary>
	/// Verifies that with data on both sides and no moment on either, the server wins.
	/// </summary>
	[Fact]
	public void Merge_NoMomentOnEitherSideAndDataOnBoth_ShouldTakeTheServer()
	{
		var merged = ProfileMerge.Merge(Local(Document(home: Place("local", DateTimeOffset.UnixEpoch))), Document(home: Place("server", DateTimeOffset.UnixEpoch)));

		merged.Document.Home!.Label.Should().Be("server");
	}

	/// <summary>
	/// Verifies that nothing on either side stays nothing.
	/// </summary>
	[Fact]
	public void Merge_EmptyOnBothSides_ShouldStayEmpty()
	{
		var merged = ProfileMerge.Merge(Local(Document()), Document());

		ProfileMerge.AreSame(merged.Document, Document()).Should().BeTrue();
		merged.Times.Should().Be(SectionTimes.None);
	}
	#endregion

	#region Favourites Tests
	/// <summary>
	/// Verifies that the favourites are one section: the newer list replaces the other as a whole, with no place-by-place union.
	/// </summary>
	[Fact]
	public void Merge_Favourites_ShouldTakeTheNewerListAsAWhole()
	{
		var local = Local(Document(favourites: Favourites(New, "a", "b")), new SectionTimes(null, null, New, null, null));

		var merged = ProfileMerge.Merge(local, Document(favourites: Favourites(Old, "c")));

		merged.Document.Favourites.Places.Select(place => place.Label).Should().Equal("a", "b");
		merged.Times.Favourites.Should().Be(New);
	}

	/// <summary>
	/// Verifies that an empty favourites list the user left (newer) replaces an older server list.
	/// </summary>
	[Fact]
	public void Merge_FavouritesEmptiedLocallyAndNewer_ShouldEmptyTheServerList()
	{
		var local = Local(Document(favourites: Favourites(New)), new SectionTimes(null, null, New, null, null));

		var merged = ProfileMerge.Merge(local, Document(favourites: Favourites(Old, "c")));

		merged.Document.Favourites.Places.Should().BeEmpty();
	}
	#endregion

	#region Vehicle And Card Tests
	/// <summary>
	/// Verifies that the vehicle and the card are decided on their own moments.
	/// </summary>
	[Fact]
	public void Merge_VehicleAndCard_ShouldBeDecidedSeparately()
	{
		var local = Local(Document(vehicle: Vehicle("local", New), card: Card("111", Old)), new SectionTimes(null, null, null, New, Old));

		var merged = ProfileMerge.Merge(local, Document(vehicle: Vehicle("server", Old), card: Card("222", New)));

		merged.Document.Vehicle!.Make.Should().Be("local");
		merged.Document.Card!.Number.Should().Be("222");
	}

	/// <summary>
	/// Verifies that a card the user removed (newer) removes the older server card.
	/// </summary>
	[Fact]
	public void Merge_CardClearedLocallyAndNewer_ShouldRemoveTheServerCard()
	{
		var local = Local(Document(), new SectionTimes(null, null, null, null, New));

		var merged = ProfileMerge.Merge(local, Document(card: Card("222", Old)));

		merged.Document.Card.Should().BeNull();
	}
	#endregion

	#region Comparison Tests
	/// <summary>
	/// Verifies that two documents with equal values but separate lists are the same.
	/// </summary>
	[Fact]
	public void AreSame_EqualValuesInSeparateInstances_ShouldBeTrue()
	{
		var first = Document(Place("home", New), null, Favourites(New, "a"), Vehicle("make", New), Card("111", New));
		var second = Document(Place("home", New), null, Favourites(New, "a"), Vehicle("make", New), Card("111", New));

		ProfileMerge.AreSame(first, second).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a different value in any section, or a different moment, makes two documents differ.
	/// </summary>
	[Fact]
	public void AreSame_AnyDifference_ShouldBeFalse()
	{
		var baseline = Document(Place("home", New), Place("work", New), Favourites(New, "a"), Vehicle("make", New), Card("111", New));

		ProfileMerge.AreSame(baseline, Document(Place("other", New), Place("work", New), Favourites(New, "a"), Vehicle("make", New), Card("111", New))).Should().BeFalse();
		ProfileMerge.AreSame(baseline, Document(Place("home", New), null, Favourites(New, "a"), Vehicle("make", New), Card("111", New))).Should().BeFalse();
		ProfileMerge.AreSame(baseline, Document(Place("home", New), Place("work", New), Favourites(New, "a", "b"), Vehicle("make", New), Card("111", New))).Should().BeFalse();
		ProfileMerge.AreSame(baseline, Document(Place("home", New), Place("work", New), Favourites(New, "a"), Vehicle("other", New), Card("111", New))).Should().BeFalse();
		ProfileMerge.AreSame(baseline, Document(Place("home", New), Place("work", New), Favourites(New, "a"), Vehicle("make", New), Card("222", New))).Should().BeFalse();
		ProfileMerge.AreSame(baseline, Document(Place("home", Old), Place("work", New), Favourites(New, "a"), Vehicle("make", New), Card("111", New))).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the comparison of data alone ignores the moment of a section.
	/// </summary>
	[Fact]
	public void SameData_MomentDiffers_ShouldBeTrueForEverySection()
	{
		var first = Document(Place("home", New), Place("work", New), Favourites(New, "a"), Vehicle("make", New), Card("111", New));
		var second = Document(Place("home", Old), Place("work", Old), Favourites(Old, "a"), Vehicle("make", Old), Card("111", Old));

		Enum.GetValues<SyncSection>().Should().OnlyContain(section => ProfileMerge.SameData(section, first, second));
	}

	/// <summary>
	/// Verifies that the moments of a document read the epoch and absent sections as none.
	/// </summary>
	[Fact]
	public void TimesOf_EpochAndAbsentSections_ShouldBeNone()
	{
		var document = Document(Place("home", New), null, Favourites(DateTimeOffset.UnixEpoch), null, Card("111", Old));

		var times = ProfileMerge.TimesOf(document);

		times.Should().Be(new SectionTimes(New, null, null, null, Old));
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	[Fact]
	public void Merge_NullArguments_ShouldThrowArgumentNullException()
	{
		var noLocal = () => ProfileMerge.Merge(null!, Document()); // null on purpose: proves the guard clause rejects it
		var noServer = () => ProfileMerge.Merge(Local(Document()), null!); // null on purpose: proves the guard clause rejects it

		noLocal.Should().Throw<ArgumentNullException>();
		noServer.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
