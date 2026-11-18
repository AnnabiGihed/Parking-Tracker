using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.Testing.Device;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.Device.Tests.Features.Shared.Encryption;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="FieldCipher"/>: a value round-trips, two encryptions of one text differ, a changed value,
///              another key, another column, another row or another key identifier is refused, and a missing, short or failing key
///              gives a failed result instead of an exception; a key that cannot be read now is told apart from a key that does not exist.
/// </summary>
public sealed class FieldCipherTests
{
	#region Test Infrastructure
	/// <summary>
	/// Builds a cipher over a key store.
	/// </summary>
	/// <param name="keys">The key store.</param>
	/// <param name="anyRows">What the probe answers to whether an encrypted row exists; none by default.</param>
	/// <returns>The cipher.</returns>
	private static FieldCipher Cipher(IDeviceKeyStore keys, bool anyRows = false) => new(keys, Probe(Result.Success(anyRows)), NullLogger<FieldCipher>.Instance);

	/// <summary>
	/// Builds a probe that gives a fixed answer.
	/// </summary>
	/// <param name="answer">The answer.</param>
	/// <returns>The probe.</returns>
	private static IEncryptedDataProbe Probe(Result<bool> answer)
	{
		var probe = Substitute.For<IEncryptedDataProbe>();
		probe.AnyAsync(Arg.Any<CancellationToken>()).Returns(answer);
		return probe;
	}
	#endregion

	#region Static Instances
	/// <summary>
	/// Where the values of most tests live.
	/// </summary>
	private static readonly FieldBinding Binding = new("resident_cards", "plate", "11111111-1111-1111-1111-111111111111");
	#endregion
	#region Round Trip Tests
	/// <summary>
	/// Verifies that an encrypted text decrypts to the original, including text that is not ASCII, and starts with the key identifier.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EncryptDecrypt_Text_ShouldRoundTripWithKeyIdPrefix()
	{
		var cipher = Cipher(new InMemoryKeyStore("k7"));

		var sealedText = await cipher.EncryptAsync("1ABC234 é中", Binding);
		var opened = await cipher.DecryptAsync(sealedText.Value, Binding);

		sealedText.IsSuccess.Should().BeTrue();
		sealedText.Value.Should().StartWith("k7.");
		sealedText.Value.Should().NotContain("1ABC234");
		opened.Value.Should().Be("1ABC234 é中");
	}

	/// <summary>
	/// Verifies that every encryption uses a fresh nonce, so that equal texts never give equal values.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Encrypt_SameTextTwice_ShouldGiveDifferentValues()
	{
		var cipher = Cipher(new InMemoryKeyStore());

		var first = await cipher.EncryptAsync("same", Binding);
		var second = await cipher.EncryptAsync("same", Binding);

		first.Value.Should().NotBe(second.Value);
	}

	/// <summary>
	/// Verifies that an empty text round-trips.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EncryptDecrypt_EmptyText_ShouldRoundTrip()
	{
		var cipher = Cipher(new InMemoryKeyStore());

		var opened = await cipher.DecryptAsync((await cipher.EncryptAsync(string.Empty, Binding)).Value, Binding);

		opened.Value.Should().BeEmpty();
	}
	#endregion

	#region Binding Tests
	/// <summary>
	/// Verifies that a value moved to another column, another row or another table is refused by the authentication tag, so that a
	/// ciphertext cannot be swapped between fields.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Decrypt_ValueMovedToAnotherField_ShouldFailAuthentication()
	{
		var cipher = Cipher(new InMemoryKeyStore());
		var stored = (await cipher.EncryptAsync("1ABC234", Binding)).Value;

		var otherColumn = await cipher.DecryptAsync(stored, Binding with { Column = "card_number" });
		var otherRow = await cipher.DecryptAsync(stored, Binding with { RowId = "22222222-2222-2222-2222-222222222222" });
		var otherTable = await cipher.DecryptAsync(stored, Binding with { Table = "parking_sessions" });

		otherColumn.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
		otherRow.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
		otherTable.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
		(await cipher.DecryptAsync(stored, Binding)).Value.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that the key identifier in front of a value is authenticated too: relabelling a value with the identifier of another
	/// key that the phone holds is refused, also when both keys have the same material.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Decrypt_KeyIdentifierChanged_ShouldFailAuthentication()
	{
		var material = RandomNumberGenerator.GetBytes(32);
		var stored = (await Cipher(new InMemoryKeyStore(new DeviceKey("k1", material))).EncryptAsync("secret", Binding)).Value;
		var relabelled = "k2" + stored[stored.IndexOf('.', StringComparison.Ordinal)..];

		var opened = await Cipher(new InMemoryKeyStore(new DeviceKey("k2", material))).DecryptAsync(relabelled, Binding);

		opened.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}
	#endregion

	#region Refusal Tests
	/// <summary>
	/// Verifies that a value of which one byte changed is refused by the authentication tag.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Decrypt_TamperedValue_ShouldFailAuthentication()
	{
		var cipher = Cipher(new InMemoryKeyStore());
		var stored = (await cipher.EncryptAsync("secret", Binding)).Value;
		var dot = stored.IndexOf('.', StringComparison.Ordinal);
		var payload = Convert.FromBase64String(stored[(dot + 1)..]);
		payload[^1] ^= 0x01;
		var tampered = stored[..(dot + 1)] + Convert.ToBase64String(payload);

		var opened = await cipher.DecryptAsync(tampered, Binding);

		opened.IsFailure.Should().BeTrue();
		opened.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that a value written with one key is refused by another key that carries the same identifier.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Decrypt_WrongKeyWithSameId_ShouldFailAuthentication()
	{
		var stored = (await Cipher(new InMemoryKeyStore()).EncryptAsync("secret", Binding)).Value;

		var opened = await Cipher(new InMemoryKeyStore()).DecryptAsync(stored, Binding);

		opened.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that a value that names a key this phone does not have, as after a restore on a new phone, reads as "key missing" and
	/// not as "key unavailable".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Decrypt_UnknownKeyId_ShouldFailWithKeyMissing()
	{
		var stored = (await Cipher(new InMemoryKeyStore("old")).EncryptAsync("secret", Binding)).Value;

		var opened = await Cipher(new InMemoryKeyStore("new")).DecryptAsync(stored, Binding);

		opened.Error.Should().Be(DeviceDatabaseErrors.KeyMissing);
	}

	/// <summary>
	/// Verifies that text that is not in the format of the cipher is refused as malformed.
	/// </summary>
	/// <param name="stored">The stored text.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("")]
	[InlineData("nodot")]
	[InlineData(".AAAA")]
	[InlineData("k1.not base64!")]
	[InlineData("k1.AAAA")]
	public async Task Decrypt_MalformedText_ShouldFailAsMalformed(string stored)
	{
		var opened = await Cipher(new InMemoryKeyStore()).DecryptAsync(stored, Binding);

		opened.Error.Should().Be(DeviceDatabaseErrors.CiphertextMalformed);
	}
	#endregion

	#region Key Lost Tests
	/// <summary>
	/// Verifies that a storage without a key gets a new key when no encrypted row exists, and that the value then decrypts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Encrypt_NoKeyAndNoRows_ShouldCreateTheKey()
	{
		var keys = InMemoryKeyStore.WithoutKey();
		var cipher = Cipher(keys);

		var sealedText = await cipher.EncryptAsync("secret", Binding);
		var opened = await cipher.DecryptAsync(sealedText.Value, Binding);

		keys.CreatedCount.Should().Be(1);
		opened.Value.Should().Be("secret");
	}

	/// <summary>
	/// Verifies that a storage without a key is not given a new key while encrypted rows exist: the failure says the key was lost.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Encrypt_NoKeyButRowsExist_ShouldFailWithKeyLostAndCreateNothing()
	{
		var keys = InMemoryKeyStore.WithoutKey();

		var sealedText = await Cipher(keys, anyRows: true).EncryptAsync("secret", Binding);

		DeviceDataErrors.IsKeyLost(sealedText).Should().BeTrue();
		keys.CreatedCount.Should().Be(0);
	}

	/// <summary>
	/// Verifies that a probe that cannot tell is not read as "no rows": no key is created and the failure is "unavailable".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Encrypt_NoKeyAndProbeFails_ShouldFailWithKeyUnavailableAndCreateNothing()
	{
		var keys = InMemoryKeyStore.WithoutKey();
		var cipher = new FieldCipher(keys, Probe(Result.Failure<bool>(DeviceDatabaseErrors.ReadFailed)), NullLogger<FieldCipher>.Instance);

		var sealedText = await cipher.EncryptAsync("secret", Binding);

		sealedText.Error.Should().Be(DeviceDataErrors.KeyUnavailable);
		keys.CreatedCount.Should().Be(0);
	}
	#endregion

	#region Key Unavailable Tests
	/// <summary>
	/// Verifies that a key that is not 256 bits long is not used for encrypting.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Encrypt_ShortKey_ShouldFailWithKeyUnavailable()
	{
		var cipher = Cipher(new InMemoryKeyStore(new DeviceKey("k1", new byte[16])));

		var sealedText = await cipher.EncryptAsync("secret", Binding);

		sealedText.Error.Should().Be(DeviceDataErrors.KeyUnavailable);
	}

	/// <summary>
	/// Verifies that a key store that fails, as the platform storage does when its key is lost, gives a failed result for encrypting
	/// and for decrypting, and that the cause is logged.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_KeyStoreThrows_ShouldFailWithKeyUnavailableAndLogTheCause()
	{
		var keys = Substitute.For<IDeviceKeyStore>();
		keys.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns<Task<Result<DeviceKey>>>(_ => throw new CryptographicException("key lost"));
		keys.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<Result<DeviceKey>>>(_ => throw new CryptographicException("key lost"));
		var logger = Substitute.For<ILogger<FieldCipher>>();
		logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
		var cipher = new FieldCipher(keys, Probe(Result.Success(false)), logger);

		var sealedText = await cipher.EncryptAsync("secret", Binding);
		var opened = await cipher.DecryptAsync("k1." + Convert.ToBase64String(new byte[40]), Binding);

		sealedText.Error.Should().Be(DeviceDataErrors.KeyUnavailable);
		opened.Error.Should().Be(DeviceDataErrors.KeyUnavailable);
		logger.ReceivedCalls().Count(call => string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)).Should().Be(2);
	}

	/// <summary>
	/// Verifies that the failure of a key store that answers with a failed result is passed on as it is.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Encrypt_KeyStoreAnswersUnavailable_ShouldPassTheFailureOn()
	{
		var keys = Substitute.For<IDeviceKeyStore>();
		keys.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable));

		var sealedText = await Cipher(keys).EncryptAsync("secret", Binding);

		DeviceDataErrors.IsKeyUnavailable(sealedText).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the constructor and the methods reject missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_NullArguments_ShouldThrow()
	{
		var cipher = Cipher(new InMemoryKeyStore());

		var noKeys = () => new FieldCipher(null!, Probe(Result.Success(false)), NullLogger<FieldCipher>.Instance); // null on purpose: proves the guard clause rejects it
		var noProbe = () => new FieldCipher(new InMemoryKeyStore(), null!, NullLogger<FieldCipher>.Instance); // null on purpose: proves the guard clause rejects it
		var noLogger = () => new FieldCipher(new InMemoryKeyStore(), Probe(Result.Success(false)), null!); // null on purpose: proves the guard clause rejects it
		var noText = () => cipher.EncryptAsync(null!, Binding); // null on purpose: proves the guard clause rejects it
		var noStored = () => cipher.DecryptAsync(null!, Binding); // null on purpose: proves the guard clause rejects it

		noKeys.Should().Throw<ArgumentNullException>();
		noProbe.Should().Throw<ArgumentNullException>();
		noLogger.Should().Throw<ArgumentNullException>();
		await noText.Should().ThrowAsync<ArgumentNullException>();
		await noStored.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion
}
