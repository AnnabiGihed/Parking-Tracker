using ParkingTracker.Application.Features.Accounts.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Tests.Features.Accounts.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Google sign-in that answers from a fixed result and remembers the nonce it was given and how often its state was cleared.
/// </summary>
internal sealed class FakeGoogleSignIn : IGoogleSignIn
{
	#region Fields
	/// <summary>
	/// How many times the state was cleared.
	/// </summary>
	private int clears;
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsAvailable { get; set; } = true;

	/// <summary>
	/// Gets or sets what a sign-in answers with.
	/// </summary>
	public Result<string> Answer { get; set; } = Result.Success("google-id-token");

	/// <summary>
	/// Gets the nonce of the last sign-in, or <see langword="null"/> when none ran.
	/// </summary>
	public string? LastNonce { get; private set; }

	/// <summary>
	/// Gets how many times the state was cleared.
	/// </summary>
	public int Clears => Volatile.Read(ref clears);
	#endregion

	#region IGoogleSignIn Implementation
	/// <inheritdoc />
	public Task<Result<string>> SignInAsync(string nonce, CancellationToken cancellationToken = default)
	{
		LastNonce = nonce;
		return Task.FromResult(Answer);
	}

	/// <inheritdoc />
	public Task ClearStateAsync(CancellationToken cancellationToken = default)
	{
		_ = Interlocked.Increment(ref clears);
		return Task.CompletedTask;
	}
	#endregion
}
