using System.Globalization;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.ViewModels.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ISessionTokenStore"/> over an encrypted key-value storage (ADR-0015): the host passes the Android keystore storage,
///              never the device database. The three values are written refresh token first, so that an interrupted write leaves a refresh
///              token that works rather than a spent one. A read that finds one of the values missing or unreadable reports nobody signed in.
///              Storage operations cannot be interrupted; cancellation is checked before each operation. No token is logged.
/// </summary>
public sealed class SecureSessionTokenStore : ISessionTokenStore
{
	#region Constants
	/// <summary>
	/// The key of the access token.
	/// </summary>
	public const string AccessTokenKey = "session.accessToken";

	/// <summary>
	/// The key of the moment the access token expires, in the round-trip format.
	/// </summary>
	public const string ExpiryKey = "session.accessTokenExpiresAt";

	/// <summary>
	/// The key of the refresh token.
	/// </summary>
	public const string RefreshTokenKey = "session.refreshToken";
	#endregion

	#region Fields
	/// <summary>
	/// The encrypted storage.
	/// </summary>
	private readonly IKeyValueStorage storage;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SecureSessionTokenStore"/> class.
	/// </summary>
	/// <param name="storage">The encrypted storage.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="storage"/> is <see langword="null"/>.</exception>
	public SecureSessionTokenStore(IKeyValueStorage storage)
	{
		ArgumentNullException.ThrowIfNull(storage);
		this.storage = storage;
	}
	#endregion

	#region ISessionTokenStore Implementation
	/// <inheritdoc />
	public async Task<SessionTokens?> GetAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var access = await storage.ReadAsync(AccessTokenKey).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		var expiry = await storage.ReadAsync(ExpiryKey).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		var refresh = await storage.ReadAsync(RefreshTokenKey).ConfigureAwait(false);
		if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)
			|| !DateTimeOffset.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
		{
			return null;
		}

		return new SessionTokens(access, expiresAt, refresh);
	}

	/// <inheritdoc />
	public async Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tokens);
		cancellationToken.ThrowIfCancellationRequested();
		await storage.WriteAsync(RefreshTokenKey, tokens.RefreshToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		await storage.WriteAsync(ExpiryKey, tokens.AccessTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture)).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		await storage.WriteAsync(AccessTokenKey, tokens.AccessToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task ClearAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await storage.DeleteAsync(AccessTokenKey).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		await storage.DeleteAsync(ExpiryKey).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		await storage.DeleteAsync(RefreshTokenKey).ConfigureAwait(false);
	}
	#endregion
}
