using Microsoft.Maui.Storage;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The encrypted storage of the settings store over MAUI <c>SecureStorage</c> (Android keystore), where the card lives
///              (ADR-0004). The settings port is synchronous, so the asynchronous calls run on the thread pool and are awaited there:
///              never on the thread that called, which could be the UI thread. A value that cannot be decrypted, for example after a
///              restore on another phone, is dropped and read as "nothing saved", so that the app asks for the card again.
/// </summary>
public sealed class SecureValueStorage : IKeyValueStorage
{
	#region IKeyValueStorage Implementation
	/// <inheritdoc />
	public string? Read(string key)
	{
		try
		{
			return Task.Run(() => SecureStorage.Default.GetAsync(key)).GetAwaiter().GetResult();
		}
		catch (Exception)
		{
			// The keystore key is gone or the value is unreadable: nothing can be recovered, so forget it.
			SecureStorage.Default.Remove(key);
			return null;
		}
	}

	/// <inheritdoc />
	public void Write(string key, string value)
	{
		Task.Run(() => SecureStorage.Default.SetAsync(key, value)).GetAwaiter().GetResult();
	}

	/// <inheritdoc />
	public void Delete(string key)
	{
		SecureStorage.Default.Remove(key);
	}
	#endregion
}
