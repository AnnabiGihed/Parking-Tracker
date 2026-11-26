using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Services;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="SaveProfileCommand"/> (ADR-0032): writes the document as JSON, encrypts it under the revision the row
///              will have (1 for a create, the next one for a replacement; the cipher authenticates the revision), and stores it.
///              The write must be based on what is stored: a create over an existing profile, and a replacement at a revision that is
///              not the stored one or finds no profile, are <see cref="ProfileErrors.PreconditionFailed"/>. A save that loses a race, the
///              revision being the concurrency token of a replacement and the account being the key of a create, fails the unit of
///              work with a conflict, which is the same precondition failure and not a server error; the caller reads again.
/// </summary>
public sealed class SaveProfileCommandHandler : ICommandHandler<SaveProfileCommand, SaveProfileResponse>
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
	/// Encrypts the document.
	/// </summary>
	private readonly IProfileCipher cipher;

	/// <summary>
	/// Saves the staged profile.
	/// </summary>
	private readonly IUnitOfWork unitOfWork;

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SaveProfileCommandHandler"/> class.
	/// </summary>
	/// <param name="accounts">The accounts.</param>
	/// <param name="profiles">The encrypted profiles.</param>
	/// <param name="cipher">Encrypts the document.</param>
	/// <param name="unitOfWork">Saves the staged profile.</param>
	/// <param name="clock">The clock.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public SaveProfileCommandHandler(IUserAccountRepository accounts, IAccountProfileRepository profiles, IProfileCipher cipher, IUnitOfWork unitOfWork, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(profiles);
		ArgumentNullException.ThrowIfNull(cipher);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(clock);
		this.accounts = accounts;
		this.profiles = profiles;
		this.cipher = cipher;
		this.unitOfWork = unitOfWork;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<SaveProfileResponse>> Handle(SaveProfileCommand request, CancellationToken cancellationToken)
	{
		if (request.AccountId == Guid.Empty)
		{
			return Result.Failure<SaveProfileResponse>(SessionErrors.AccountUnknown, ResultExceptionType.AuthenticationRequired);
		}

		var account = await accounts.FindByIdAsync(new UserAccountId(request.AccountId), cancellationToken).ConfigureAwait(false);
		if (account is null)
		{
			return Result.Failure<SaveProfileResponse>(SessionErrors.AccountUnknown, ResultExceptionType.AuthenticationRequired);
		}

		var plain = ProfileDocumentJson.Serialize(request.Document);
		if (plain.Length > EncryptedProfile.MaximumPlainBytes)
		{
			return Result.Failure<SaveProfileResponse>(ProfileErrors.TooLarge, ResultExceptionType.ValidationError);
		}

		var existing = await profiles.FindByIdAsync(account.Id, cancellationToken).ConfigureAwait(false);
		var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
		var staged = request.Condition.IsCreateOnly
			? await CreateAsync(account.Id, existing, request.Document.SchemaVersion, plain, today, cancellationToken).ConfigureAwait(false)
			: await ReplaceAsync(account.Id, existing, request.Condition.ExpectedRevision!.Value, request.Document.SchemaVersion, plain, today, cancellationToken).ConfigureAwait(false);
		if (staged.IsFailure)
		{
			return Result.Failure<SaveProfileResponse>(staged.Error, staged.ResultExceptionType);
		}

		var save = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		if (save.IsSuccess)
		{
			return new SaveProfileResponse(staged.Value);
		}

		return save.ResultExceptionType == ResultExceptionType.Conflict
			? Result.Failure<SaveProfileResponse>(ProfileErrors.PreconditionFailed, ResultExceptionType.Conflict)
			: Result.Failure<SaveProfileResponse>(save.Error, save.ResultExceptionType);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Stages the first profile of the account.
	/// </summary>
	/// <param name="accountId">The account.</param>
	/// <param name="existing">The profile that is stored, or <see langword="null"/> for none.</param>
	/// <param name="schemaVersion">The schema version of the document.</param>
	/// <param name="plain">The document as JSON.</param>
	/// <param name="today">The day of the write.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>The revision the profile will have, or the failure.</returns>
	private async Task<Result<long>> CreateAsync(UserAccountId accountId, AccountProfile? existing, int schemaVersion, byte[] plain, DateOnly today, CancellationToken cancellationToken)
	{
		if (existing is not null)
		{
			return Result.Failure<long>(ProfileErrors.PreconditionFailed, ResultExceptionType.Conflict);
		}

		var encrypted = cipher.Encrypt(accountId, AccountProfile.FirstRevision, schemaVersion, plain);
		var created = AccountProfile.Create(accountId, schemaVersion, encrypted, today);
		if (created.IsFailure)
		{
			return Result.Failure<long>(created.Error, ResultExceptionType.ValidationError);
		}

		await profiles.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
		return created.Value.Revision;
	}

	/// <summary>
	/// Stages the replacement of the stored profile.
	/// </summary>
	/// <param name="accountId">The account.</param>
	/// <param name="existing">The profile that is stored, or <see langword="null"/> for none.</param>
	/// <param name="expectedRevision">The revision the write replaces.</param>
	/// <param name="schemaVersion">The schema version of the document.</param>
	/// <param name="plain">The document as JSON.</param>
	/// <param name="today">The day of the write.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>The revision the profile will have, or the failure.</returns>
	private async Task<Result<long>> ReplaceAsync(UserAccountId accountId, AccountProfile? existing, long expectedRevision, int schemaVersion, byte[] plain, DateOnly today, CancellationToken cancellationToken)
	{
		if (existing is null || existing.Revision != expectedRevision)
		{
			return Result.Failure<long>(ProfileErrors.PreconditionFailed, ResultExceptionType.Conflict);
		}

		var encrypted = cipher.Encrypt(accountId, existing.NextRevision, schemaVersion, plain);
		var replaced = existing.Replace(schemaVersion, encrypted, today);
		if (replaced.IsFailure)
		{
			return Result.Failure<long>(replaced.Error, ResultExceptionType.ValidationError);
		}

		await profiles.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
		return existing.Revision;
	}
	#endregion
}
