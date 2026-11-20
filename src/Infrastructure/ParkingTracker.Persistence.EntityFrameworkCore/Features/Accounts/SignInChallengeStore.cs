using Microsoft.EntityFrameworkCore;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The nonces of the sign-in on PostgreSQL. Each operation is one statement that takes effect at once, outside the
///              unit of work: a nonce is consumed by a single DELETE that checks the expiry in the same statement, so two requests
///              cannot both consume it, and a consumed nonce stays consumed when a later step of the sign-in fails.
/// </summary>
internal sealed class SignInChallengeStore : ISignInChallengeStore
{
	#region Fields
	/// <summary>
	/// The server database context.
	/// </summary>
	private readonly DbContext dbContext;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SignInChallengeStore"/> class.
	/// </summary>
	/// <param name="dbContext">The server database context.</param>
	public SignInChallengeStore(DbContext dbContext)
	{
		this.dbContext = dbContext;
	}
	#endregion

	#region ISignInChallengeStore Implementation
	/// <inheritdoc />
	public async Task CreateAsync(string nonce, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
		var expiredBefore = now.ToUniversalTime();
		var expiry = expiresAt.ToUniversalTime();
		_ = await dbContext.Database
			.ExecuteSqlInterpolatedAsync($"DELETE FROM sign_in_challenges WHERE \"ExpiresAtUtc\" <= {expiredBefore}", cancellationToken)
			.ConfigureAwait(false);
		_ = await dbContext.Database
			.ExecuteSqlInterpolatedAsync($"INSERT INTO sign_in_challenges (\"Nonce\", \"ExpiresAtUtc\") VALUES ({nonce}, {expiry})", cancellationToken)
			.ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<bool> ConsumeAsync(string nonce, DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(nonce);
		var moment = now.ToUniversalTime();
		var deleted = await dbContext.Database
			.ExecuteSqlInterpolatedAsync($"DELETE FROM sign_in_challenges WHERE \"Nonce\" = {nonce} AND \"ExpiresAtUtc\" > {moment}", cancellationToken)
			.ConfigureAwait(false);
		return deleted == 1;
	}
	#endregion
}
