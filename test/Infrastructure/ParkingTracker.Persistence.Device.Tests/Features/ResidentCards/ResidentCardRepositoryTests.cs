using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.ResidentCards.Errors;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Persistence.Device.Features.ResidentCards;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.Testing.Device;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.Device.Tests.Features.ResidentCards;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="ResidentCardRepository"/> against a real SQLite
///              file: a card round-trips with the number and plate encrypted at rest, a stored card that breaks a rule gives a failed
///              result, a field that cannot be decrypted or moved to another column fails the read instead of being dropped, a key that
///              cannot be read now is not "no card", the card keeps its identifier, the database holds one card, and changes only reach
///              the database at commit.
/// </summary>
public sealed class ResidentCardRepositoryTests
{
	#region Round Trip Tests
	/// <summary>
	/// Verifies that a card of the City of Brussels comes back with every value, and that the number and the plate are not stored as
	/// plain text.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_CityCard_ShouldRoundTripWithEncryptedNumberAndPlate()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var card = TestData.CityCard();

		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, card))).IsSuccess.Should().BeTrue();
		var loaded = await db.Cards.GetAsync();

		loaded.IsSuccess.Should().BeTrue();
		loaded.Value.ToParkingCard().Should().BeEquivalentTo(card.ToParkingCard());
		loaded.Value.ToParkingCard().Number.Should().Be("B012-345-678");
		loaded.Value.ToParkingCard().Plate.Should().Be("1ABC234");
		(await db.ScalarAsync<string>("SELECT card_number FROM resident_cards")).Should().StartWith("k1.").And.NotContain("B012");
		(await db.ScalarAsync<string>("SELECT plate FROM resident_cards")).Should().StartWith("k1.").And.NotContain("1ABC234");
	}

	/// <summary>
	/// Verifies that a card of Ixelles with areas, a border zone, no number, no plate and no dates round-trips, and that a missing
	/// number or plate is stored as null rather than as an encrypted empty text.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_IxellesCardWithoutNumberAndPlate_ShouldRoundTripWithNullFields()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var card = TestData.IxellesCard();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, card));

		var loaded = await db.Cards.GetAsync();

		loaded.Value.ToParkingCard().Should().BeEquivalentTo(card.ToParkingCard());
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards WHERE card_number IS NULL AND plate IS NULL AND valid_from IS NULL")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that nothing is stored until the unit of work commits.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_BeforeCommit_ShouldStoreNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			await db.Cards.AddAsync(scope, TestData.CityCard());
		}

		var loaded = await db.Cards.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Should().Be(DeviceDataErrors.NotStored);
	}

	/// <summary>
	/// Verifies that a card keeps its identifier through a save and a load.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_Card_ShouldKeepTheIdentifier()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var card = TestData.CityCard();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, card));

		var loaded = await db.Cards.GetAsync();

		loaded.Value.Id.Should().Be(card.Id);
		(await db.ScalarAsync<string>("SELECT id FROM resident_cards")).Should().Be(card.Id.Value.ToString("D"));
	}

	/// <summary>
	/// Verifies that an add does nothing when a card is stored already, so that a newer card is never overwritten, and that the
	/// database refuses a second row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_CardStoredAlready_ShouldKeepTheStoredCardAndTheDatabaseHoldsOnlyOne()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.IxellesCard()));

		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()))).IsSuccess.Should().BeTrue();
		var second = () => db.ExecuteAsync("INSERT INTO resident_cards (id, municipality, tolerances, mailles, lims) VALUES ('33333333-3333-3333-3333-333333333333', 'bruxelles', '[]', '[]', '[]')");

		(await db.Cards.GetAsync()).Value.ToParkingCard().Municipality.Should().Be("ixelles");
		await second.Should().ThrowAsync<SQLite.SQLiteException>();
	}

	/// <summary>
	/// Verifies that an empty database gives "no card", not a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NothingStored_ShouldSucceedWithNull()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var loaded = await db.Cards.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Should().Be(DeviceDataErrors.NotStored);
	}

	/// <summary>
	/// Verifies that an update replaces the stored card, also one that was loaded before and so has another identifier.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Update_StoredCard_ShouldReplaceIt()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		var replacement = TestData.IxellesCard();

		(await db.ApplyAsync(scope => db.Cards.UpdateAsync(scope, replacement))).IsSuccess.Should().BeTrue();
		var loaded = await db.Cards.GetAsync();

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(1);
		loaded.Value.ToParkingCard().Municipality.Should().Be("ixelles");
	}

	/// <summary>
	/// Verifies that a removal deletes the card, and that removing when none is stored is harmless.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_StoredCard_ShouldDeleteItAtCommit()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));

		(await db.ApplyRemovalAsync(db.Cards.Remove)).IsSuccess.Should().BeTrue();
		(await db.ApplyRemovalAsync(db.Cards.Remove)).IsSuccess.Should().BeTrue();

		(await db.Cards.GetAsync()).Error.Should().Be(DeviceDataErrors.NotStored);
	}
	#endregion

	#region Invalid Row Tests
	/// <summary>
	/// Verifies that a stored card that breaks a rule of its municipality (an area outside the prefixes) gives the failed result of the
	/// aggregate and no card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_AreaOutsideTheMunicipality_ShouldFailWithTheDomainError()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("INSERT INTO resident_cards (id, municipality, tolerances, mailles, lims) VALUES ('11111111-1111-1111-1111-111111111111', 'ixelles', '[]', '[\"STGL_1\"]', '[]')");

		var loaded = await db.Cards.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Should().Be(ResidentCardErrors.MailleOutsideMunicipality("STGL_1"));
	}

	/// <summary>
	/// Verifies that a stored card of a municipality that the reference data does not know is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_UnknownMunicipality_ShouldFailWithMismatch()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("INSERT INTO resident_cards (id, municipality, tolerances, mailles, lims) VALUES ('11111111-1111-1111-1111-111111111111', 'atlantis', '[]', '[]', '[]')");

		var loaded = await db.Cards.GetAsync();

		loaded.Error.Should().Be(ResidentCardErrors.MunicipalityMismatch);
	}

	/// <summary>
	/// Verifies that a stored card whose dates are reversed is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_ReversedPeriod_ShouldFailWithTheDomainError()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("INSERT INTO resident_cards (id, municipality, valid_from, valid_to, tolerances, mailles, lims) VALUES ('11111111-1111-1111-1111-111111111111', 'bruxelles', '2027-01-01', '2026-01-01', '[]', '[]', '[]')");

		var loaded = await db.Cards.GetAsync();

		loaded.Error.Should().Be(ResidentCardErrors.PeriodReversed);
	}

	/// <summary>
	/// Verifies that a date or a list that cannot be read is refused as an invalid row.
	/// </summary>
	/// <param name="column">The column that holds the damaged text.</param>
	/// <param name="value">The damaged text.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("valid_from", "yesterday")]
	[InlineData("tolerances", "not json")]
	[InlineData("lims", "[1,2]")]
	public async Task Get_DamagedColumn_ShouldFailWithInvalidRow(string column, string value)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("INSERT INTO resident_cards (id, municipality, tolerances, mailles, lims) VALUES ('11111111-1111-1111-1111-111111111111', 'bruxelles', '[]', '[]', '[]')");
		await db.ExecuteAsync($"UPDATE resident_cards SET {column} = '{value}'");

		var loaded = await db.Cards.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Code.Should().Be(DeviceDatabaseErrors.RowInvalid("resident_cards", string.Empty).Code);
	}

	/// <summary>
	/// Verifies that a failed reference data load fails the read with that failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_ReferenceDataMissing_ShouldFail()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		var missing = new Error("ReferenceData.Missing", "missing");
		db.ReferenceData.GetReferenceData().Returns(Result.Failure<ReferenceDataSet>(missing));

		var loaded = await db.Cards.GetAsync();

		loaded.Error.Should().Be(missing);
	}

	/// <summary>
	/// Verifies that a database without the tables gives a failed read, not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NoTable_ShouldFailWithReadFailed()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var loaded = await db.Cards.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.ReadFailed);
	}
	#endregion

	#region Missing Key Tests
	/// <summary>
	/// Verifies that a card whose number and plate cannot be decrypted (another key with the same identifier, or no such key, as on
	/// another phone) fails the read, and is never returned with the two fields dropped.
	/// </summary>
	/// <param name="keyId">The identifier of the key of the other phone.</param>
	/// <param name="code">The code of the failure.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("k1", "DeviceDatabase.Cipher.Rejected")]
	[InlineData("other", "DeviceDatabase.Key.Missing")]
	public async Task Get_KeyOfAnotherPhone_ShouldFailTheRead(string keyId, string code)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		var otherPhone = new ResidentCardRepository(db.Database, new FieldCipher(new InMemoryKeyStore(keyId), db.Probe, NullLogger<FieldCipher>.Instance), db.ReferenceData, NullLogger<ResidentCardRepository>.Instance);

		var loaded = await otherPhone.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Code.Should().Be(code);
	}

	/// <summary>
	/// Verifies that a key that cannot be read now fails the read with <see cref="DeviceDataErrors.KeyUnavailable"/>, which is not
	/// "nothing stored", and that the card reads again once the key is back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_KeyUnavailable_ShouldFailWithKeyUnavailableAndRecoverLater()
	{
		var keys = new InMemoryKeyStore();
		await using var db = await DeviceTestDatabase.CreateAsync(keys);
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		keys.IsUnavailable = true;

		var unavailable = await db.Cards.GetAsync();
		keys.IsUnavailable = false;
		var back = await db.Cards.GetAsync();

		DeviceDataErrors.IsKeyUnavailable(unavailable).Should().BeTrue();
		DeviceDataErrors.IsNotStored(unavailable).Should().BeFalse();
		back.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a save with a key that cannot be read now fails and stages nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_KeyUnavailable_ShouldFailWithKeyUnavailableAndStoreNothing()
	{
		var keys = new InMemoryKeyStore { IsUnavailable = true };
		await using var db = await DeviceTestDatabase.CreateAsync(keys);

		var added = await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));

		DeviceDataErrors.IsKeyUnavailable(added).Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that the encrypted number and plate cannot be swapped: the associated data binds each value to its column.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NumberAndPlateSwapped_ShouldFailAuthentication()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		await db.ExecuteAsync("UPDATE resident_cards SET card_number = plate");

		var loaded = await db.Cards.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that an encrypted field copied from another row (another identifier) is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_PlateOfAnotherRow_ShouldFailAuthentication()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		await db.ExecuteAsync("UPDATE resident_cards SET id = '44444444-4444-4444-4444-444444444444'");

		var loaded = await db.Cards.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}
	#endregion

	#region Argument Tests
	/// <summary>
	/// Verifies that the constructor and the methods reject missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			var noCard = () => db.Cards.AddAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noUpdate = () => db.Cards.UpdateAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noScope = () => db.Cards.AddAsync(null!, TestData.CityCard()); // null on purpose: proves the guard clause rejects it

			await noCard.Should().ThrowAsync<ArgumentNullException>();
			await noUpdate.Should().ThrowAsync<ArgumentNullException>();
			await noScope.Should().ThrowAsync<ArgumentNullException>();
		}
	}
	#endregion
}
