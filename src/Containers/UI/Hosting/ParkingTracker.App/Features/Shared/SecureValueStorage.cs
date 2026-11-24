using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The encrypted storage of the settings store over MAUI <c>SecureStorage</c> (Android keystore), where the card lives
///              (ADR-0004). A value that cannot be decrypted, for example after a restore on another phone or a change of the lock
///              screen, is dropped and read as "nothing saved", so that the app asks for the card again. Any other failure keeps the
///              value, because it may be readable next time, and is logged.
/// </summary>
public sealed partial class SecureValueStorage : IKeyValueStorage
{
	#region Fields
	/// <summary>
	/// The log, where a value that could not be read is reported.
	/// </summary>
	private readonly ILogger<SecureValueStorage> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SecureValueStorage"/> class.
	/// </summary>
	/// <param name="logger">The log, where a value that could not be read is reported.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
	public SecureValueStorage(ILogger<SecureValueStorage> logger)
	{
		ArgumentNullException.ThrowIfNull(logger);
		this.logger = logger;
	}
	#endregion

	#region IKeyValueStorage Implementation
	/// <inheritdoc />
	public async Task<string?> ReadAsync(string key)
	{
		try
		{
			return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is CryptographicException or InvalidOperationException or Java.Security.GeneralSecurityException)
		{
			// The keystore key is gone or the value does not decrypt with it: nothing can be recovered, so forget it.
			LogUndecryptableValue(logger, exception);
			SecureStorage.Default.Remove(key);
			return null;
		}
		catch (Exception exception)
		{
			// Not known to be permanent: keep the value, so that the next read can still succeed.
			LogReadFailed(logger, exception);
			return null;
		}
	}

	/// <inheritdoc />
	/// <exception cref="IOException">Thrown when secure storage cannot save the value.</exception>
	public async Task WriteAsync(string key, string value)
	{
		try
		{
			await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is CryptographicException or InvalidOperationException or Java.Security.GeneralSecurityException)
		{
			throw new IOException("The value could not be saved in secure storage.", exception);
		}
	}

	/// <inheritdoc />
	/// <exception cref="IOException">Thrown when secure storage cannot remove the value.</exception>
	public Task DeleteAsync(string key)
	{
		try
		{
			SecureStorage.Default.Remove(key);
		}
		catch (Exception exception) when (exception is CryptographicException or InvalidOperationException or Java.Security.GeneralSecurityException)
		{
			throw new IOException("The value could not be removed from secure storage.", exception);
		}

		return Task.CompletedTask;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that a saved value could not be decrypted and was removed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "A value of the encrypted storage cannot be decrypted and was removed")]
	private static partial void LogUndecryptableValue(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that a saved value could not be read for a reason that is not known to be permanent.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading a value of the encrypted storage failed; the value was kept")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion
}
