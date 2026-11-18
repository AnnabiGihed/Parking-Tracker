using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Shared;
using SavedPlacesList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Domain.Tests.Features.SavedPlaces.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SavedPlacesList"/>: at most one home, one work place and 20 favourites, every place needs a
///              label and a point inside the Brussels box, and a refused change leaves the list as it was.
/// </summary>
public sealed class SavedPlacesTests
{
	#region Static Instances
	/// <summary>
	/// A point in the centre of Brussels.
	/// </summary>
	private static readonly GeoPoint Centre = new(50.8503, 4.3517);

	/// <summary>
	/// A point in Antwerp, outside the box.
	/// </summary>
	private static readonly GeoPoint Antwerp = new(51.2194, 4.4025);
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that a new list is empty and raises no event.
	/// </summary>
	[Fact]
	public void Create_NewList_ShouldBeEmpty()
	{
		var list = SavedPlacesList.Create();

		list.Places.Should().BeEmpty();
		list.Home.Should().BeNull();
		list.Work.Should().BeNull();
		list.GetDomainEvents().Should().BeEmpty();
	}
	#endregion

	#region Home And Work Tests
	/// <summary>
	/// Verifies that a home is saved with a trimmed label and address.
	/// </summary>
	[Fact]
	public void SetHome_ValidPlace_ShouldSaveIt()
	{
		var list = SavedPlacesList.Create();

		var result = list.SetHome(" Home ", Centre, " Rue Neuve 1 ");

		result.IsSuccess.Should().BeTrue();
		list.Home!.Label.Should().Be("Home");
		list.Home.AddressText.Should().Be("Rue Neuve 1");
		list.Home.Point.Should().Be(Centre);
		list.Home.Kind.Should().Be(PlaceKind.Home);
	}

	/// <summary>
	/// Verifies that setting the home again replaces it, so there is still only one.
	/// </summary>
	[Fact]
	public void SetHome_Twice_ShouldKeepOnlyTheLast()
	{
		var list = SavedPlacesList.Create();
		list.SetHome("Old", Centre, null);

		list.SetHome("New", new GeoPoint(50.84, 4.35), null);

		list.Places.Should().ContainSingle(place => place.Kind == PlaceKind.Home);
		list.Home!.Label.Should().Be("New");
		list.Home.AddressText.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that home and work are kept apart.
	/// </summary>
	[Fact]
	public void SetWork_AfterHome_ShouldKeepBoth()
	{
		var list = SavedPlacesList.Create();
		list.SetHome("Home", Centre, null);

		var result = list.SetWork("Office", new GeoPoint(50.86, 4.36), null);

		result.IsSuccess.Should().BeTrue();
		list.Home!.Label.Should().Be("Home");
		list.Work!.Label.Should().Be("Office");
	}

	/// <summary>
	/// Verifies that a refused home leaves the old one.
	/// </summary>
	/// <param name="label">The label.</param>
	/// <param name="latitude">The latitude of the point.</param>
	/// <param name="longitude">The longitude of the point.</param>
	/// <param name="code">The expected error code.</param>
	[Theory]
	[InlineData(" ", 50.85, 4.35, "SavedPlaces.Label.Required")]
	[InlineData(null, 50.85, 4.35, "SavedPlaces.Label.Required")]
	[InlineData("Far", 51.2194, 4.4025, "SavedPlaces.Point.OutsideBrussels")]
	public void SetHome_Refused_ShouldKeepTheOldHome(string? label, double latitude, double longitude, string code)
	{
		var list = SavedPlacesList.Create();
		list.SetHome("Old", Centre, null);

		var result = list.SetHome(label, new GeoPoint(latitude, longitude), null);

		result.Error.Code.Should().Be(code);
		result.ResultExceptionType.Should().Be(ResultExceptionType.ValidationError);
		list.Home!.Label.Should().Be("Old");
	}

	/// <summary>
	/// Verifies that the edges of the box count as inside.
	/// </summary>
	[Fact]
	public void SetWork_PointOnTheEdge_ShouldBeAccepted()
	{
		var list = SavedPlacesList.Create();

		var result = list.SetWork("Edge", new GeoPoint(SavedPlacesList.BrusselsBox.South, SavedPlacesList.BrusselsBox.West), null);

		result.IsSuccess.Should().BeTrue();
	}
	#endregion

	#region Favourite Tests
	/// <summary>
	/// Verifies that favourites are added with their own identifiers and in order.
	/// </summary>
	[Fact]
	public void AddFavourite_ValidPlaces_ShouldKeepThemInOrder()
	{
		var list = SavedPlacesList.Create();

		var first = list.AddFavourite("Bakery", Centre, null);
		var second = list.AddFavourite("Park", Centre, null);

		first.Value.Should().NotBe(second.Value);
		list.Places.Select(place => place.Label).Should().Equal("Bakery", "Park");
	}

	/// <summary>
	/// Verifies that the 21st favourite is refused as a conflict and the home does not count.
	/// </summary>
	[Fact]
	public void AddFavourite_TwentyOne_ShouldFailWithConflict()
	{
		var list = SavedPlacesList.Create();
		list.SetHome("Home", Centre, null);
		for (var index = 1; index <= SavedPlacesList.MaximumFavourites; index++)
		{
			list.AddFavourite("Place " + index, Centre, null).IsSuccess.Should().BeTrue();
		}

		var result = list.AddFavourite("One too many", Centre, null);

		result.Error.Should().Be(SavedPlacesErrors.TooManyFavourites);
		result.ResultExceptionType.Should().Be(ResultExceptionType.Conflict);
		list.Places.Should().HaveCount(21);
	}

	/// <summary>
	/// Verifies that a favourite outside the box or without label is refused.
	/// </summary>
	[Fact]
	public void AddFavourite_OutsideTheBoxOrWithoutLabel_ShouldFail()
	{
		var list = SavedPlacesList.Create();

		list.AddFavourite("Antwerp", Antwerp, null).Error.Should().Be(SavedPlacesErrors.PointOutsideBrussels);
		list.AddFavourite("", Centre, null).Error.Should().Be(SavedPlacesErrors.LabelRequired);
		list.Places.Should().BeEmpty();
	}
	#endregion

	#region Remove Tests
	/// <summary>
	/// Verifies that a saved place can be removed.
	/// </summary>
	[Fact]
	public void Remove_SavedPlace_ShouldRemoveIt()
	{
		var list = SavedPlacesList.Create();
		var id = list.AddFavourite("Bakery", Centre, null).Value;

		var result = list.Remove(id);

		result.IsSuccess.Should().BeTrue();
		list.Places.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that removing an unknown place is a not-found failure and a missing id a programming error.
	/// </summary>
	[Fact]
	public void Remove_UnknownPlace_ShouldFailWithNotFound()
	{
		var list = SavedPlacesList.Create();
		var withoutId = () => list.Remove(null!);

		var result = list.Remove(SavedPlaceId.New());

		result.Error.Should().Be(SavedPlacesErrors.PlaceNotFound);
		result.ResultExceptionType.Should().Be(ResultExceptionType.NotFound);
		withoutId.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Identifier Tests
	/// <summary>
	/// Verifies that identifiers are never empty and sort like their value.
	/// </summary>
	[Fact]
	public void Ids_Empty_ShouldBeRefusedAndOrderingFollowTheValue()
	{
		var lowList = new SavedPlacesId(new Guid(1, 0, 0, new byte[8]));
		var highList = new SavedPlacesId(new Guid(2, 0, 0, new byte[8]));
		var lowPlace = new SavedPlaceId(new Guid(1, 0, 0, new byte[8]));
		var highPlace = new SavedPlaceId(new Guid(2, 0, 0, new byte[8]));
		var emptyList = () => new SavedPlacesId(Guid.Empty);
		var emptyPlace = () => new SavedPlaceId(Guid.Empty);

		emptyList.Should().Throw<ArgumentException>();
		emptyPlace.Should().Throw<ArgumentException>();
		(lowList < highList).Should().BeTrue();
		(lowList <= highList).Should().BeTrue();
		(highList > lowList).Should().BeTrue();
		(highList >= lowList).Should().BeTrue();
		(lowPlace < highPlace).Should().BeTrue();
		(lowPlace <= highPlace).Should().BeTrue();
		(highPlace > lowPlace).Should().BeTrue();
		(highPlace >= lowPlace).Should().BeTrue();
	}
	#endregion

	#region Restore Tests
	/// <summary>
	/// Verifies that stored places come back with the stored identifiers, in order, and that the list raises no event.
	/// </summary>
	[Fact]
	public void Restore_StoredPlaces_ShouldKeepTheIdentifiersAndOrder()
	{
		var listId = SavedPlacesId.New();
		var home = new StoredPlace(SavedPlaceId.New(), PlaceKind.Home, " Home ", Centre, " Rue Neuve 1 ");
		var favourite = new StoredPlace(SavedPlaceId.New(), PlaceKind.Favourite, "Cinema", Centre, null);

		var result = SavedPlacesList.Restore(listId, [home, favourite]);

		result.IsSuccess.Should().BeTrue();
		result.Value.Id.Should().Be(listId);
		result.Value.Places.Select(place => place.Id).Should().Equal(home.Id, favourite.Id);
		result.Value.Home!.Label.Should().Be("Home");
		result.Value.Home.AddressText.Should().Be("Rue Neuve 1");
		result.Value.GetDomainEvents().Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that stored places are checked like new ones: label, box, kind, repeats and the favourites limit.
	/// </summary>
	[Fact]
	public void Restore_PlacesThatBreakARule_ShouldFail()
	{
		var listId = SavedPlacesId.New();
		var home = new StoredPlace(SavedPlaceId.New(), PlaceKind.Home, "Home", Centre, null);

		SavedPlacesList.Restore(listId, [home with { Label = " " }]).Error.Should().Be(SavedPlacesErrors.LabelRequired);
		SavedPlacesList.Restore(listId, [home with { Point = Antwerp }]).Error.Should().Be(SavedPlacesErrors.PointOutsideBrussels);
		SavedPlacesList.Restore(listId, [home with { Kind = (PlaceKind)9 }]).Error.Should().Be(SavedPlacesErrors.KindUnknown);
		SavedPlacesList.Restore(listId, [home, home with { Id = SavedPlaceId.New() }]).Error.Should().Be(SavedPlacesErrors.PlaceDuplicated);
		SavedPlacesList.Restore(listId, [home, home with { Kind = PlaceKind.Favourite }]).Error.Should().Be(SavedPlacesErrors.PlaceDuplicated);
		var tooMany = Enumerable.Range(0, SavedPlacesList.MaximumFavourites + 1).Select(_ => home with { Id = SavedPlaceId.New(), Kind = PlaceKind.Favourite });
		SavedPlacesList.Restore(listId, tooMany).Error.Should().Be(SavedPlacesErrors.TooManyFavourites);
	}

	/// <summary>
	/// Verifies that an empty stored list restores and that missing arguments are rejected.
	/// </summary>
	[Fact]
	public void Restore_EmptyListAndNullArguments_ShouldBehave()
	{
		SavedPlacesList.Restore(SavedPlacesId.New(), []).Value.Places.Should().BeEmpty();

		var noId = () => SavedPlacesList.Restore(null!, []); // null on purpose: proves the guard clause rejects it
		var noPlaces = () => SavedPlacesList.Restore(SavedPlacesId.New(), null!); // null on purpose: proves the guard clause rejects it

		noId.Should().Throw<ArgumentNullException>();
		noPlaces.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
