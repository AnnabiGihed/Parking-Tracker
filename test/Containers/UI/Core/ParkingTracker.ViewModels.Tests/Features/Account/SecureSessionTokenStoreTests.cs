using NSubstitute;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SecureSessionTokenStore"/>: tokens round-trip through the storage, a missing or damaged value reads as
///              nobody signed in, clearing removes every key, and the text of the tokens record shows no secret.
/// </summary>
public sealed class SecureSessionTokenStoreTests
{
	#region Test Infrastructure
	/// <summary>
	/// Tokens with a recognisable secret in each field.
	/// </summary>
	private static readonly SessionTokens Tokens = new("access-secret", new DateTimeOffset(2026, 11, 17, 10, 15, 0, TimeSpan.Zero), "refresh-secret");
	#endregion

	#region Round Trip Tests
	/// <summary>
	/// Verifies that saved tokens are read back unchanged.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveAsync_ThenGet_ShouldReturnTheSameTokens()
	{
		var store = new SecureSessionTokenStore(new InMemoryKeyValueStorage());

		await store.SaveAsync(Tokens);

		(await store.GetAsync()).Should().Be(Tokens);
	}

	/// <summary>
	/// Verifies that nothing saved reads as nobody signed in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_NothingSaved_ShouldReturnNull()
	{
		var store = new SecureSessionTokenStore(new InMemoryKeyValueStorage());

		(await store.GetAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a lost value (for example one the keystore could not decrypt) reads as nobody signed in.
	/// </summary>
	/// <param name="lostKey">The key that is missing.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(SecureSessionTokenStore.AccessTokenKey)]
	[InlineData(SecureSessionTokenStore.RefreshTokenKey)]
	[InlineData(SecureSessionTokenStore.ExpiryKey)]
	public async Task GetAsync_OneValueMissing_ShouldReturnNull(string lostKey)
	{
		var storage = new InMemoryKeyValueStorage();
		var store = new SecureSessionTokenStore(storage);
		await store.SaveAsync(Tokens);
		await storage.DeleteAsync(lostKey);

		(await store.GetAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that an expiry that is not a date reads as nobody signed in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_DamagedExpiry_ShouldReturnNull()
	{
		var storage = new InMemoryKeyValueStorage();
		var store = new SecureSessionTokenStore(storage);
		await store.SaveAsync(Tokens);
		await storage.WriteAsync(SecureSessionTokenStore.ExpiryKey, "not a date");

		(await store.GetAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that clearing removes every key.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ClearAsync_ShouldRemoveEveryKey()
	{
		var storage = new InMemoryKeyValueStorage();
		var store = new SecureSessionTokenStore(storage);
		await store.SaveAsync(Tokens);

		await store.ClearAsync();

		storage.Values.Should().BeEmpty();
	}
	#endregion

	#region Cancellation Tests
	/// <summary>
	/// Verifies that a canceled read does not access secure storage.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_Canceled_ShouldNotReadStorage()
	{
		var storage = Substitute.For<IKeyValueStorage>();
		var store = new SecureSessionTokenStore(storage);
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		var read = () => store.GetAsync(cancellation.Token);

		await read.Should().ThrowAsync<OperationCanceledException>();
		await storage.DidNotReceive().ReadAsync(Arg.Any<string>());
	}

	/// <summary>
	/// Verifies that cancellation before saving preserves existing secure values.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveAsync_Canceled_ShouldNotWriteStorage()
	{
		var storage = Substitute.For<IKeyValueStorage>();
		var store = new SecureSessionTokenStore(storage);
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		var save = () => store.SaveAsync(Tokens, cancellation.Token);

		await save.Should().ThrowAsync<OperationCanceledException>();
		await storage.DidNotReceive().WriteAsync(Arg.Any<string>(), Arg.Any<string>());
	}

	/// <summary>
	/// Verifies that cancellation before clearing preserves existing secure values.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ClearAsync_Canceled_ShouldNotDeleteStorage()
	{
		var storage = Substitute.For<IKeyValueStorage>();
		var store = new SecureSessionTokenStore(storage);
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		var clear = () => store.ClearAsync(cancellation.Token);

		await clear.Should().ThrowAsync<OperationCanceledException>();
		await storage.DidNotReceive().DeleteAsync(Arg.Any<string>());
	}
	#endregion

	#region Secrecy Tests
	/// <summary>
	/// Verifies that the text of the tokens shows neither token, so that logging the record leaks nothing.
	/// </summary>
	[Fact]
	public void ToString_ShouldShowNoToken()
	{
		var text = Tokens.ToString();

		text.Should().NotContain("access-secret").And.NotContain("refresh-secret");
	}

	/// <summary>
	/// Verifies that a missing argument is refused.
	/// </summary>
	[Fact]
	public void Constructor_NullStorage_ShouldThrowArgumentNullException()
	{
		var create = () => new SecureSessionTokenStore(null!); // null on purpose: proves the guard clause rejects it

		create.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
