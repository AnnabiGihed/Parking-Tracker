namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The smallest view of the storage of the phone that <see cref="SettingsStore"/> needs: text values under a key. The
///              host implements it twice, over the encrypted storage and over the plain preferences, so that everything that
///              decides which key goes where, and in which JSON, stays in code that tests can run on any computer.
/// </summary>
public interface IKeyValueStorage
{
	#region Methods
	/// <summary>
	/// Reads a value.
	/// </summary>
	/// <param name="key">The key, for example <c>pt.card</c>.</param>
	/// <returns>The value, or <see langword="null"/> when none is saved or the storage cannot read it.</returns>
	Task<string?> ReadAsync(string key);

	/// <summary>
	/// Saves a value, replacing the saved one.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <param name="value">The text to keep.</param>
	/// <returns>A task that completes when the value is saved.</returns>
	Task WriteAsync(string key, string value);

	/// <summary>
	/// Removes a value; nothing happens when none is saved.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <returns>A task that completes when the value is removed.</returns>
	Task DeleteAsync(string key);
	#endregion
}
