using ParkingTracker.Domain.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Primitives;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.Accounts.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The profile document of one account (ADR-0032), kept encrypted and opaque: the revision that the clients synchronise
///              on, the schema version of the document, the key that encrypted it and the encrypted bytes. It shares the identifier of
///              its account, so an account has at most one. Replacing the document raises the revision by one; the caller encrypts
///              under <see cref="NextRevision"/> because the revision is part of what the cipher authenticates. The aggregate holds
///              no clock, and dispatches no domain event.
/// </summary>
public sealed class AccountProfile : LightweightAggregateRoot<UserAccountId>
{
	#region Constants
	/// <summary>
	/// The revision of a profile that was written once.
	/// </summary>
	public const long FirstRevision = 1;
	#endregion

	#region Properties
	/// <summary>
	/// Gets the revision of the document, 1 for the first write; a write that is based on an older revision is refused by the store.
	/// </summary>
	public long Revision { get; private set; }

	/// <summary>
	/// Gets the schema version of the plain document.
	/// </summary>
	public int SchemaVersion { get; private set; }

	/// <summary>
	/// Gets the identifier of the key that encrypted the payload.
	/// </summary>
	public string KeyId { get; private set; } = default!;

	/// <summary>
	/// Gets the encrypted document.
	/// </summary>
	public byte[] Payload { get; private set; } = default!;

	/// <summary>
	/// Gets the day of the last write, to the day.
	/// </summary>
	public DateOnly UpdatedOn { get; private set; }

	/// <summary>
	/// Gets the revision that the next replacement gets, the one to encrypt it under.
	/// </summary>
	public long NextRevision => Revision + 1;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccountProfile"/> class for the persistence layer.
	/// </summary>
	private AccountProfile()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="AccountProfile"/> class.
	/// </summary>
	/// <param name="accountId">The account the profile belongs to.</param>
	/// <param name="schemaVersion">The schema version of the plain document.</param>
	/// <param name="encrypted">The encrypted document.</param>
	/// <param name="today">The day of the write.</param>
	private AccountProfile(UserAccountId accountId, int schemaVersion, EncryptedProfile encrypted, DateOnly today)
		: base(accountId)
	{
		Revision = FirstRevision;
		SchemaVersion = schemaVersion;
		KeyId = encrypted.KeyId;
		Payload = encrypted.Payload;
		UpdatedOn = today;
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the profile of an account that has none, at revision 1. The document must have been encrypted under revision 1.
	/// </summary>
	/// <param name="accountId">The account the profile belongs to.</param>
	/// <param name="schemaVersion">The schema version of the plain document.</param>
	/// <param name="encrypted">The encrypted document.</param>
	/// <param name="today">The day of the write.</param>
	/// <returns>The profile, or a failure when the schema version is below 1.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="accountId"/> or <paramref name="encrypted"/> is <see langword="null"/>.</exception>
	public static Result<AccountProfile> Create(UserAccountId accountId, int schemaVersion, EncryptedProfile encrypted, DateOnly today)
	{
		ArgumentNullException.ThrowIfNull(accountId);
		ArgumentNullException.ThrowIfNull(encrypted);
		return schemaVersion < 1
			? Result.Failure<AccountProfile>(AccountErrors.ProfileSchemaVersionInvalid)
			: new AccountProfile(accountId, schemaVersion, encrypted, today);
	}
	#endregion

	#region Domain Behaviours
	/// <summary>
	/// Replaces the document and raises the revision by one. The document must have been encrypted under <see cref="NextRevision"/>.
	/// </summary>
	/// <param name="schemaVersion">The schema version of the plain document.</param>
	/// <param name="encrypted">The new encrypted document.</param>
	/// <param name="today">The day of the write.</param>
	/// <returns>Success, or a failure when the schema version is below 1.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="encrypted"/> is <see langword="null"/>.</exception>
	public Result Replace(int schemaVersion, EncryptedProfile encrypted, DateOnly today)
	{
		ArgumentNullException.ThrowIfNull(encrypted);
		if (schemaVersion < 1)
		{
			return Result.Failure(AccountErrors.ProfileSchemaVersionInvalid);
		}

		Revision++;
		SchemaVersion = schemaVersion;
		KeyId = encrypted.KeyId;
		Payload = encrypted.Payload;
		UpdatedOn = today;
		return Result.Success();
	}
	#endregion
}
