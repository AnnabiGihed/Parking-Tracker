using Microsoft.Extensions.Logging;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Services;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Queries.GetProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="GetProfileQuery"/> (ADR-0032): reads the stored profile of the account, decrypts it under the revision
///              it is stored with and reads the document. An account that no longer exists is an authentication failure, like in
///              <c>GetMe</c>, so the old access token of an erased account stops working; an account without a profile is a not-found.
///              A profile that cannot be decrypted or read is a failure that tells the caller nothing of the cause; the cause is only logged.
/// </summary>
public sealed partial class GetProfileQueryHandler : IQueryHandler<GetProfileQuery, GetProfileResponse>
{
	#region Fields
	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts;

	/// <summary>
	/// The encrypted profiles.
	/// </summary>
	private readonly IAccountProfileRepository profiles;

	/// <summary>
	/// Decrypts the stored document.
	/// </summary>
	private readonly IProfileCipher cipher;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<GetProfileQueryHandler> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetProfileQueryHandler"/> class.
	/// </summary>
	/// <param name="accounts">The accounts.</param>
	/// <param name="profiles">The encrypted profiles.</param>
	/// <param name="cipher">Decrypts the stored document.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public GetProfileQueryHandler(IUserAccountRepository accounts, IAccountProfileRepository profiles, IProfileCipher cipher, ILogger<GetProfileQueryHandler> logger)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(profiles);
		ArgumentNullException.ThrowIfNull(cipher);
		ArgumentNullException.ThrowIfNull(logger);
		this.accounts = accounts;
		this.profiles = profiles;
		this.cipher = cipher;
		this.logger = logger;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<GetProfileResponse>> Handle(GetProfileQuery request, CancellationToken cancellationToken)
	{
		if (request.AccountId == Guid.Empty)
		{
			return Result.Failure<GetProfileResponse>(SessionErrors.AccountUnknown, ResultExceptionType.AuthenticationRequired);
		}

		var account = await accounts.FindByIdAsync(new UserAccountId(request.AccountId), cancellationToken).ConfigureAwait(false);
		if (account is null)
		{
			return Result.Failure<GetProfileResponse>(SessionErrors.AccountUnknown, ResultExceptionType.AuthenticationRequired);
		}

		var profile = await profiles.FindByIdAsync(account.Id, cancellationToken).ConfigureAwait(false);
		return profile is null
			? Result.Failure<GetProfileResponse>(ProfileErrors.NotFound, ResultExceptionType.NotFound)
			: Read(account.Id, profile);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Decrypts a stored profile and reads its document.
	/// </summary>
	/// <param name="accountId">The account the profile belongs to.</param>
	/// <param name="profile">The stored profile.</param>
	/// <returns>The document with its revision, or the failure of the decryption or of the reading.</returns>
	private Result<GetProfileResponse> Read(UserAccountId accountId, AccountProfile profile)
	{
		var stored = EncryptedProfile.Create(profile.KeyId, profile.Payload);
		if (stored.IsFailure)
		{
			LogUnreadable(logger, ProfileErrors.Unreadable.Code);
			return Result.Failure<GetProfileResponse>(ProfileErrors.Unreadable);
		}

		var plain = cipher.Decrypt(accountId, profile.Revision, profile.SchemaVersion, stored.Value);
		if (plain.IsFailure)
		{
			LogUnreadable(logger, plain.Error.Code);
			return Result.Failure<GetProfileResponse>(plain.Error);
		}

		var document = ProfileDocumentJson.TryDeserialize(plain.Value);
		if (document is null)
		{
			LogUnreadable(logger, ProfileErrors.Unreadable.Code);
			return Result.Failure<GetProfileResponse>(ProfileErrors.Unreadable);
		}

		return new GetProfileResponse(document, profile.Revision);
	}

	/// <summary>
	/// Writes the log line of a stored profile that cannot be read, with the error code and nothing of the profile.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="errorCode">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "A stored profile cannot be read: {ErrorCode}")]
	private static partial void LogUnreadable(ILogger logger, string errorCode);
	#endregion
}
