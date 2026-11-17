using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SettingsStore"/>: the card goes to the encrypted storage and everything else to the plain one,
///              under the keys of the first version, and the card is read from the storage only once.
/// </summary>
public sealed class SettingsStoreTests
{
	#region Card Tests
	/// <summary>
	/// Verifies that the card is written to the encrypted storage only, under <c>pt.card</c>.
	/// </summary>
	[Fact]
	public void SaveCard_ShouldWriteToTheEncryptedStorageOnly()
	{
		var (store, secure, plain) = Create();

		store.SaveCard(new ParkingCard("bruxelles", "louise", new CardPeriod(null, null), [], [], []));

		secure.Values.Should().ContainKey("pt.card");
		plain.Values.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a card copied from the old storage, without a municipality, is read as a City of Brussels card.
	/// </summary>
	[Fact]
	public void GetCard_OldCardWithoutMunicipality_ShouldReadAsBruxelles()
	{
		var (store, secure, _) = Create();
		secure.Values["pt.card"] = """{"number":"TEST","plate":"1TST000","sector":"louise","from":"2026-01-01","to":"2099-12-31","tolerances":[]}""";

		store.GetCard()!.Municipality.Should().Be("bruxelles");
	}

	/// <summary>
	/// Verifies that the card is read from the storage once and then served from memory, also after it was saved.
	/// </summary>
	[Fact]
	public void GetCard_CalledTwice_ShouldReadTheStorageOnce()
	{
		var (store, secure, _) = Create();
		secure.Values["pt.card"] = """{"municipality":"ixelles","mailles":["IXL01"]}""";

		store.GetCard();
		store.GetCard();

		secure.Reads.Should().Be(1);
	}

	/// <summary>
	/// Verifies that a saved card is what the next read returns, and that clearing removes it.
	/// </summary>
	[Fact]
	public void SaveCard_ThenClear_ShouldReadBackThenNothing()
	{
		var (store, secure, _) = Create();
		var card = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXL01"], [], null, null);

		store.SaveCard(card);
		store.GetCard().Should().BeEquivalentTo(card);
		store.ClearCard();

		store.GetCard().Should().BeNull();
		secure.Values.Should().BeEmpty();
	}
	#endregion

	#region Plain Values Tests
	/// <summary>
	/// Verifies that the car, the layers, the welcome flag and the snooze go to the plain storage under their keys and read back.
	/// </summary>
	[Fact]
	public void PlainValues_SaveThenGet_ShouldRoundTripUnderTheOldKeys()
	{
		var (store, secure, plain) = Create();
		var car = new ParkedCar(new GeoPoint(50.5, 4.25), DateTimeOffset.FromUnixTimeMilliseconds(5000), "PAY", string.Empty);
		var until = DateTimeOffset.FromUnixTimeMilliseconds(1764000000000);

		store.SaveCar(car);
		store.SaveLayers(new LayerSettings(Streets: false));
		store.SaveWelcomeSeen();
		store.SavePermissionSnoozeUntil(until);

		plain.Values.Keys.Should().BeEquivalentTo("pt.car", "pt.layers", "pt.welcomeSeen", "pt.permSnooze");
		secure.Values.Should().BeEmpty();
		store.GetCar().Should().Be(car);
		store.GetLayers().Should().Be(new LayerSettings(Streets: false));
		store.GetWelcomeSeen().Should().BeTrue();
		store.GetPermissionSnoozeUntil().Should().Be(until);
	}

	/// <summary>
	/// Verifies that an empty store gives the calm defaults, and that clearing the car removes it.
	/// </summary>
	[Fact]
	public void EmptyStore_ShouldGiveDefaults_AndClearCarShouldRemoveTheCar()
	{
		var (store, _, plain) = Create();

		store.GetCar().Should().BeNull();
		store.GetLayers().Should().Be(new LayerSettings());
		store.GetWelcomeSeen().Should().BeFalse();
		store.GetPermissionSnoozeUntil().Should().BeNull();

		store.SaveCar(new ParkedCar(new GeoPoint(1, 2), DateTimeOffset.UnixEpoch, string.Empty, string.Empty));
		store.ClearCar();
		plain.Values.Should().BeEmpty();
	}
	#endregion

	#region Guard Tests
	/// <summary>
	/// Verifies that missing storages and missing values are rejected.
	/// </summary>
	[Fact]
	public void Constructor_AndSave_NullArguments_ShouldThrowArgumentNullException()
	{
		var storage = new InMemoryKeyValueStorage();
		var (store, _, _) = Create();

		var noSecure = () => new SettingsStore(null!, storage); // null on purpose: proves the guard clause rejects it
		var noPlain = () => new SettingsStore(storage, null!); // null on purpose: proves the guard clause rejects it
		var noCard = () => store.SaveCard(null!); // null on purpose: proves the guard clause rejects it

		noSecure.Should().Throw<ArgumentNullException>();
		noPlain.Should().Throw<ArgumentNullException>();
		noCard.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a store over two empty in-memory storages.
	/// </summary>
	/// <returns>The store and the encrypted and plain storage behind it.</returns>
	private static (SettingsStore Store, InMemoryKeyValueStorage Secure, InMemoryKeyValueStorage Plain) Create()
	{
		var secure = new InMemoryKeyValueStorage();
		var plain = new InMemoryKeyValueStorage();
		return (new SettingsStore(secure, plain), secure, plain);
	}
	#endregion
}
