using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Application.Tests.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SettingsStoreExtensions"/>: a card, car or timer that cannot be read counts as none for the
///              callers that only decide what to show, a value that can be read is passed on, and any other failure is not swallowed.
/// </summary>
public sealed class SettingsStoreExtensionsTests
{
	#region Test Infrastructure
	/// <summary>
	/// The failure that a store gives for a saved value it cannot read.
	/// </summary>
	/// <returns>The exception.</returns>
	private static SavedDataUnreadableException Unreadable() => new(SavedDataUnreadableException.CardMessage, "DeviceData.Key.Lost", isTemporary: false);
	#endregion

	#region Card Tests
	/// <summary>
	/// Verifies that an unreadable card reads as none and a readable one is passed on.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCardOrNoneAsync_UnreadableAndReadable_ShouldGiveNoneAndThePassedOnCard()
	{
		var card = new ParkingCard("bruxelles", null, new CardPeriod(null, null), [], [], []);
		var unreadable = Substitute.For<ISettingsStore>();
		unreadable.GetCardAsync().Returns<Task<ParkingCard?>>(_ => throw Unreadable());
		var readable = Substitute.For<ISettingsStore>();
		readable.GetCardAsync().Returns(card);

		(await unreadable.GetCardOrNoneAsync()).Should().BeNull();
		(await readable.GetCardOrNoneAsync()).Should().Be(card);
	}

	/// <summary>
	/// Verifies that a failure that is not an unreadable value is not swallowed, and that a missing store is rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCardOrNoneAsync_OtherFailure_ShouldThrow()
	{
		var broken = Substitute.For<ISettingsStore>();
		broken.GetCardAsync().Returns<Task<ParkingCard?>>(_ => throw new IOException("storage gone"));
		var read = () => broken.GetCardOrNoneAsync();
		var noStore = () => SettingsStoreExtensions.GetCardOrNoneAsync(null!); // null on purpose: proves the guard clause rejects it

		await read.Should().ThrowAsync<IOException>();
		await noStore.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion

	#region Car And Timer Tests
	/// <summary>
	/// Verifies that an unreadable car and an unreadable timer read as none.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCarAndTimerOrNoneAsync_Unreadable_ShouldGiveNone()
	{
		var store = Substitute.For<ISettingsStore>();
		store.GetCarAsync().Returns<Task<ParkedCar?>>(_ => throw Unreadable());
		store.GetTimerAsync().Returns<Task<ParkingTimer?>>(_ => throw Unreadable());

		(await store.GetCarOrNoneAsync()).Should().BeNull();
		(await store.GetTimerOrNoneAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a readable car and timer are passed on, and that a missing store is rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCarAndTimerOrNoneAsync_Readable_ShouldPassThemOn()
	{
		var now = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
		var car = new ParkedCar(new GeoPoint(50.8467, 4.3525), now, "Paid parking", string.Empty);
		var timer = new ParkingTimer(now.AddMinutes(60), 60);
		var store = Substitute.For<ISettingsStore>();
		store.GetCarAsync().Returns(car);
		store.GetTimerAsync().Returns(timer);
		var noCar = () => SettingsStoreExtensions.GetCarOrNoneAsync(null!); // null on purpose: proves the guard clause rejects it
		var noTimer = () => SettingsStoreExtensions.GetTimerOrNoneAsync(null!); // null on purpose: proves the guard clause rejects it

		(await store.GetCarOrNoneAsync()).Should().Be(car);
		(await store.GetTimerOrNoneAsync()).Should().Be(timer);
		await noCar.Should().ThrowAsync<ArgumentNullException>();
		await noTimer.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion
}
