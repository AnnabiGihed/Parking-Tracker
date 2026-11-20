using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : An account repository that delegates to the real one, but holds the first read of the chosen kind of each request at
///              a barrier until both requests of a race have read, so that both start from the same state of the account before
///              either saves. Later reads of the same request are not held, so a retry after a failed save does not wait for a
///              request that has finished.
/// </summary>
internal sealed class RendezvousRepository : IUserAccountRepository
{
	#region Fields
	/// <summary>
	/// The real repository.
	/// </summary>
	private readonly IUserAccountRepository inner;

	/// <summary>
	/// The barrier both requests wait at.
	/// </summary>
	private readonly Barrier rendezvous;

	/// <summary>
	/// The kind of read that waits at the barrier.
	/// </summary>
	private readonly HeldRead held;

	/// <summary>
	/// Whether this request has met at the barrier already.
	/// </summary>
	private bool met;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="RendezvousRepository"/> class.
	/// </summary>
	/// <param name="inner">The real repository.</param>
	/// <param name="rendezvous">The barrier both requests wait at.</param>
	/// <param name="held">The kind of read that waits at the barrier.</param>
	public RendezvousRepository(IUserAccountRepository inner, Barrier rendezvous, HeldRead held)
	{
		this.inner = inner;
		this.rendezvous = rendezvous;
		this.held = held;
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The kinds of read that can wait at the barrier.
	/// </summary>
	public enum HeldRead
	{
		/// <summary>
		/// The read of an account by refresh token.
		/// </summary>
		ByRefreshToken,

		/// <summary>
		/// The read of an account by identifier.
		/// </summary>
		ById,
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Replaces the account repository of the container with one that waits at the barrier.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="rendezvous">The barrier both requests wait at.</param>
	/// <param name="held">The kind of read that waits at the barrier.</param>
	public static void Install(IServiceCollection services, Barrier rendezvous, HeldRead held)
	{
		var original = services.Single(descriptor => descriptor.ServiceType == typeof(IUserAccountRepository));
		_ = services.Remove(original);
		_ = services.AddScoped<IUserAccountRepository>(provider => new RendezvousRepository((IUserAccountRepository)original.ImplementationFactory!(provider), rendezvous, held));
	}
	#endregion

	#region IUserAccountRepository Implementation
	/// <inheritdoc />
	public async Task<UserAccount?> FindByRefreshTokenHashAsync(RefreshTokenHash tokenHash, CancellationToken cancellationToken = default)
	{
		var account = await inner.FindByRefreshTokenHashAsync(tokenHash, cancellationToken).ConfigureAwait(false);
		await MeetAsync(HeldRead.ByRefreshToken, cancellationToken).ConfigureAwait(false);
		return account;
	}

	/// <inheritdoc />
	public Task<UserAccount?> ReloadByRefreshTokenHashAsync(RefreshTokenHash tokenHash, CancellationToken cancellationToken = default)
	{
		return inner.ReloadByRefreshTokenHashAsync(tokenHash, cancellationToken);
	}

	/// <inheritdoc />
	public Task<UserAccount?> FindBySubjectHashAsync(SubjectHash subjectHash, CancellationToken cancellationToken = default)
	{
		return inner.FindBySubjectHashAsync(subjectHash, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<UserAccount?> FindByIdAsync(UserAccountId id, CancellationToken ct = default)
	{
		var account = await inner.FindByIdAsync(id, ct).ConfigureAwait(false);
		await MeetAsync(HeldRead.ById, ct).ConfigureAwait(false);
		return account;
	}

	/// <inheritdoc />
	public Task AddAsync(UserAccount entity, CancellationToken cancellationToken = default)
	{
		return inner.AddAsync(entity, cancellationToken);
	}

	/// <inheritdoc />
	public Task UpdateAsync(UserAccount entity, CancellationToken cancellationToken = default)
	{
		return inner.UpdateAsync(entity, cancellationToken);
	}

	/// <inheritdoc />
	public Task DeleteAsync(UserAccount entity, CancellationToken cancellationToken = default)
	{
		return inner.DeleteAsync(entity, cancellationToken);
	}

	/// <inheritdoc />
	public Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<UserAccount, bool>> predicate, CancellationToken ct = default)
	{
		return inner.ExistsAsync(predicate, ct);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Waits at the barrier the first time this request makes a read of the held kind.
	/// </summary>
	/// <param name="kind">The kind of the read just made.</param>
	/// <param name="cancellationToken">Cancels the wait.</param>
	/// <returns>A task that completes when both requests have read.</returns>
	private async Task MeetAsync(HeldRead kind, CancellationToken cancellationToken)
	{
		if (kind != held || met)
		{
			return;
		}

		met = true;
		_ = await Task.Run(() => rendezvous.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken), cancellationToken).ConfigureAwait(false);
	}
	#endregion
}
