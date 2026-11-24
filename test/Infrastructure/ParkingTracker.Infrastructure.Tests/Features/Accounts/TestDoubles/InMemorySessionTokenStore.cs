using ParkingTracker.Application.Features.Accounts.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Tests.Features.Accounts.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A token store in memory that counts its writes and clears, so that a test can prove when the session was saved, replaced or deleted
///              without a phone.
/// </summary>
internal sealed class InMemorySessionTokenStore : ISessionTokenStore
{
	#region Fields
	/// <summary>
	/// The saved tokens, or <see langword="null"/> when nobody is signed in.
	/// </summary>
	private SessionTokens? current;

	/// <summary>
	/// How many times the tokens were deleted.
	/// </summary>
	private int clears;
	#endregion

	#region Properties
	/// <summary>
	/// Gets the saved tokens, or <see langword="null"/> when nobody is signed in.
	/// </summary>
	public SessionTokens? Current => Volatile.Read(ref current);

	/// <summary>
	/// Gets how many times the tokens were deleted.
	/// </summary>
	public int Clears => Volatile.Read(ref clears);
	#endregion

	#region Public Methods
	/// <summary>
	/// Puts tokens in the store as if the user had signed in before.
	/// </summary>
	/// <param name="tokens">The tokens.</param>
	public void Seed(SessionTokens tokens) => Volatile.Write(ref current, tokens);
	#endregion

	#region ISessionTokenStore Implementation
	/// <inheritdoc />
	public Task<SessionTokens?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

	/// <inheritdoc />
	public Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken = default)
	{
		Volatile.Write(ref current, tokens);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task ClearAsync(CancellationToken cancellationToken = default)
	{
		Volatile.Write(ref current, null);
		_ = Interlocked.Increment(ref clears);
		return Task.CompletedTask;
	}
	#endregion
}
