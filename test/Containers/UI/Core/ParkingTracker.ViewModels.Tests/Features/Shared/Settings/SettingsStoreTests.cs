using NSubstitute;
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
///              under the keys of the first version, and the card is read from the storage only once (a failed read is not remembered).
/// </summary>
public sealed class SettingsStoreTests
{
	#region Card Tests
	/// <summary>
	/// Verifies that the card is written to the encrypted storage only, under <c>pt.card</c>.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveCardAsync_ShouldWriteToTheEncryptedStorageOnly()
	{
		var (store, secure, plain) = Create();

		await store.SaveCardAsync(new ParkingCard("bruxelles", "louise", new CardPeriod(null, null), [], [], []));

		secure.Values.Should().ContainKey("pt.card");
		plain.Values.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a card copied from the old storage, without a municipality, is read as a City of Brussels card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCardAsync_OldCardWithoutMunicipality_ShouldReadAsBruxelles()
	{
		var (store, secure, _) = Create();
		secure.Values["pt.card"] = """{"number":"TEST","plate":"1TST000","sector":"louise","from":"2026-01-01","to":"2099-12-31","tolerances":[]}""";

		var card = await store.GetCardAsync();

		card!.Municipality.Should().Be("bruxelles");
	}

	/// <summary>
	/// Verifies that the card is read from the storage once and then served from memory.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCardAsync_CalledTwice_ShouldReadTheStorageOnce()
	{
		var (store, secure, _) = Create();
		secure.Values["pt.card"] = """{"municipality":"ixelles","mailles":["IXL01"]}""";

		await store.GetCardAsync();
		await store.GetCardAsync();

		secure.Reads.Should().Be(1);
	}

	/// <summary>
	/// Verifies that a saved card is what the next read returns without reading the storage, and that clearing removes it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveCardAsync_ThenClear_ShouldReadBackThenNothing()
	{
		var (store, secure, _) = Create();
		var card = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXL01"], [], null, null);

		await store.SaveCardAsync(card);
		(await store.GetCardAsync()).Should().BeEquivalentTo(card);
		await store.ClearCardAsync();

		(await store.GetCardAsync()).Should().BeNull();
		secure.Values.Should().BeEmpty();
		secure.Reads.Should().Be(0);
	}

	/// <summary>
	/// Verifies that a read that failed is not remembered: the next read asks the storage again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCardAsync_AfterAFailedRead_ShouldReadTheStorageAgain()
	{
		var secure = Substitute.For<IKeyValueStorage>();
		secure.ReadAsync("pt.card").Returns(Task.FromException<string?>(new InvalidOperationException("keystore")), Task.FromResult<string?>("""{"municipality":"ixelles"}"""));
		var store = new SettingsStore(secure, new InMemoryKeyValueStorage());

		var first = () => store.GetCardAsync();
		await first.Should().ThrowAsync<InvalidOperationException>();
		var card = await store.GetCardAsync();

		card!.Municipality.Should().Be("ixelles");
	}
	#endregion

	#region Plain Values Tests
	/// <summary>
	/// Verifies that the car, the layers, the welcome flag and the snooze go to the plain storage under their keys and read back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlainValues_SaveThenGet_ShouldRoundTripUnderTheOldKeys()
	{
		var (store, secure, plain) = Create();
		var car = new ParkedCar(new GeoPoint(50.5, 4.25), DateTimeOffset.FromUnixTimeMilliseconds(5000), "PAY", string.Empty);
		var until = DateTimeOffset.FromUnixTimeMilliseconds(1764000000000);

		await store.SaveCarAsync(car);
		await store.SaveLayersAsync(new LayerSettings(Streets: false));
		await store.SaveWelcomeSeenAsync();
		await store.SavePermissionSnoozeUntilAsync(until);

		plain.Values.Keys.Should().BeEquivalentTo("pt.car", "pt.layers", "pt.welcomeSeen", "pt.permSnooze");
		secure.Values.Should().BeEmpty();
		(await store.GetCarAsync()).Should().Be(car);
		(await store.GetLayersAsync()).Should().Be(new LayerSettings(Streets: false));
		(await store.GetWelcomeSeenAsync()).Should().BeTrue();
		(await store.GetPermissionSnoozeUntilAsync()).Should().Be(until);
	}

	/// <summary>
	/// Verifies that an empty store gives the calm defaults, and that clearing the car removes it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EmptyStore_ShouldGiveDefaults_AndClearCarShouldRemoveTheCar()
	{
		var (store, _, plain) = Create();

		(await store.GetCarAsync()).Should().BeNull();
		(await store.GetLayersAsync()).Should().Be(new LayerSettings());
		(await store.GetWelcomeSeenAsync()).Should().BeFalse();
		(await store.GetPermissionSnoozeUntilAsync()).Should().BeNull();

		await store.SaveCarAsync(new ParkedCar(new GeoPoint(1, 2), DateTimeOffset.UnixEpoch, string.Empty, string.Empty));
		await store.ClearCarAsync();
		plain.Values.Should().BeEmpty();
	}
	#endregion

	#region Guard Tests
	/// <summary>
	/// Verifies that missing storages and missing values are rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Constructor_AndSave_NullArguments_ShouldThrowArgumentNullException()
	{
		var storage = new InMemoryKeyValueStorage();
		var (store, _, _) = Create();

		var noSecure = () => new SettingsStore(null!, storage); // null on purpose: proves the guard clause rejects it
		var noPlain = () => new SettingsStore(storage, null!); // null on purpose: proves the guard clause rejects it
		var noCard = () => store.SaveCardAsync(null!); // null on purpose: proves the guard clause rejects it

		noSecure.Should().Throw<ArgumentNullException>();
		noPlain.Should().Throw<ArgumentNullException>();
		await noCard.Should().ThrowAsync<ArgumentNullException>();
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
