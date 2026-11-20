using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.EndSession;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="EndSessionCommand"/> (architecture 5.5): revokes the family of the device session of the caller, so
///              its refresh tokens stop working. It is idempotent: an account or a family that is gone is a success, because the
///              goal, no usable session on that device, holds. Access tokens issued before stay valid until they expire.
/// </summary>
public sealed class EndSessionCommandHandler : ICommandHandler<EndSessionCommand>
{
	#region Fields
	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts;

	/// <summary>
	/// Saves the revocation.
	/// </summary>
	private readonly IUnitOfWork unitOfWork;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="EndSessionCommandHandler"/> class.
	/// </summary>
	/// <param name="accounts">The accounts.</param>
	/// <param name="unitOfWork">Saves the revocation.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public EndSessionCommandHandler(IUserAccountRepository accounts, IUnitOfWork unitOfWork)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		this.accounts = accounts;
		this.unitOfWork = unitOfWork;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result> Handle(EndSessionCommand request, CancellationToken cancellationToken)
	{
		var account = await accounts.FindByIdAsync(new UserAccountId(request.AccountId), cancellationToken).ConfigureAwait(false);
		if (account is null || account.EndFamily(request.FamilyId).IsFailure)
		{
			return Result.Success();
		}

		return await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}
	#endregion
}
