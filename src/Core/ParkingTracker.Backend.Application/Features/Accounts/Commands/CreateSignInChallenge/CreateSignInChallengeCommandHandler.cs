using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Services;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.CreateSignInChallenge;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="CreateSignInChallengeCommand"/>: makes a nonce of 32 random bytes, stores it for five minutes and
///              removes the expired ones in the same call, so the table never grows past what five minutes of requests make.
/// </summary>
public sealed class CreateSignInChallengeCommandHandler : ICommandHandler<CreateSignInChallengeCommand, CreateSignInChallengeResponse>
{
	#region Constants
	/// <summary>
	/// The minutes a nonce stays valid.
	/// </summary>
	public const int LifetimeMinutes = 5;
	#endregion

	#region Fields
	/// <summary>
	/// The store of the nonces.
	/// </summary>
	private readonly ISignInChallengeStore challenges;

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CreateSignInChallengeCommandHandler"/> class.
	/// </summary>
	/// <param name="challenges">The store of the nonces.</param>
	/// <param name="clock">The clock.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public CreateSignInChallengeCommandHandler(ISignInChallengeStore challenges, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(challenges);
		ArgumentNullException.ThrowIfNull(clock);
		this.challenges = challenges;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<CreateSignInChallengeResponse>> Handle(CreateSignInChallengeCommand request, CancellationToken cancellationToken)
	{
		var now = clock.GetUtcNow();
		var expiresAt = now.AddMinutes(LifetimeMinutes);
		var nonce = RandomToken.New();
		await challenges.CreateAsync(nonce, expiresAt, now, cancellationToken).ConfigureAwait(false);
		return new CreateSignInChallengeResponse(nonce, expiresAt);
	}
	#endregion
}
